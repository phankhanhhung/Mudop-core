namespace BMMDL.Runtime.Api.Services;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Events;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Rules;
using BMMDL.Runtime.Services;

/// <summary>
/// Core CRUD business logic for entity write operations (Create, Update, Replace, Delete).
/// Both DynamicEntityController and BatchController delegate to this service.
/// Does NOT manage UnitOfWork — the caller is responsible for Begin/Commit/Rollback.
/// </summary>
public class EntityWriteService : IEntityWriteService
{
    private readonly MetaModelCacheManager _cacheManager;
    private readonly IDynamicSqlBuilder _sqlBuilder;
    private readonly IQueryExecutor _queryExecutor;
    private readonly IRuleEngine _ruleEngine;
    private readonly IEventPublisher _eventPublisher;
    private readonly Handlers.DeepInsertHandler _deepInsertHandler;
    private readonly Handlers.DeepUpdateHandler _deepUpdateHandler;
    private readonly ReferentialIntegrityService _refIntegrity;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEntityValidationService _validationService;
    private readonly ILogger<EntityWriteService> _logger;

    private Task<MetaModelCache> GetCacheAsync() => _cacheManager.GetCacheAsync();

    public EntityWriteService(
        MetaModelCacheManager cacheManager,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        IRuleEngine ruleEngine,
        IEventPublisher eventPublisher,
        Handlers.DeepInsertHandler deepInsertHandler,
        Handlers.DeepUpdateHandler deepUpdateHandler,
        ReferentialIntegrityService refIntegrity,
        IUnitOfWork unitOfWork,
        IEntityValidationService validationService,
        ILogger<EntityWriteService> logger)
    {
        _cacheManager = cacheManager;
        _sqlBuilder = sqlBuilder;
        _queryExecutor = queryExecutor;
        _ruleEngine = ruleEngine;
        _eventPublisher = eventPublisher;
        _deepInsertHandler = deepInsertHandler;
        _deepUpdateHandler = deepUpdateHandler;
        _refIntegrity = refIntegrity;
        _unitOfWork = unitOfWork;
        _validationService = validationService;
        _logger = logger;
    }

    /// <summary>
    /// Create a new entity. Performs validation, rule execution, insert, event enqueue.
    /// Caller must manage UnitOfWork lifecycle.
    /// </summary>
    public async Task<EntityOperationResult> CreateAsync(
        BmEntity entityDef, string module, string entity,
        Dictionary<string, object?> data,
        RequestContext context,
        CancellationToken ct = default)
    {
        // Abstract entity guard
        if (entityDef.IsAbstract)
        {
            return EntityOperationResult.Error("AbstractEntity",
                $"Cannot create instances of abstract entity '{entity}'. Use a concrete derived entity instead.");
        }

        var effectiveTenantId = context.GetEffectiveTenantId(entityDef);

        // Singleton entity guard: only one row allowed
        if (entityDef.HasAnnotation("OData.Singleton"))
        {
            var checkOptions = new QueryOptions { TenantId = effectiveTenantId, Top = 1 };
            var (checkSql, checkParams) = _sqlBuilder.BuildSelectQuery(entityDef, checkOptions);
            var existing = await _queryExecutor.ExecuteListAsync(checkSql, checkParams, ct);
            if (existing.Count > 0)
            {
                return EntityOperationResult.Conflict(
                    $"Singleton entity '{entity}' already has an instance. Use PATCH to update it.");
            }
        }

        _logger.LogInformation("Creating {Module}.{Entity} for tenant {TenantId}", module, entity, context.TenantId);

        // Validate input data (strip computed, enums, JSONB, HasStream, associations, FK)
        var validationError = await ValidateInputData(entityDef, data, effectiveTenantId,
            isUpdate: false, validateRequiredAssociations: true, ct);
        if (validationError != null)
            return validationError;

        // Execute "before create" rules
        var evalContext = context.ToEvaluationContext();
        var ruleResult = await _ruleEngine.ExecuteBeforeCreateAsync(entityDef, data, evalContext);

        if (!ruleResult.Success)
        {
            _logger.LogWarning("Create validation failed for {Entity}: {ErrorCount} errors",
                entity, ruleResult.Errors.Count);
            return EntityOperationResult.Error("ValidationFailed",
                string.Join("; ", ruleResult.Errors.Select(e => e.Message)));
        }

        // Apply computed values from rules
        foreach (var (field, value) in ruleResult.ComputedValues)
            data[field] = value;

        Dictionary<string, object?>? created;

        // Deep insert, inheritance insert, or standard insert
        if (_deepInsertHandler.HasNestedObjects(entityDef, data))
        {
            _logger.LogInformation("Deep insert detected for {Module}.{Entity}", module, entity);
            var deepResult = await _deepInsertHandler.ExecuteAsync(entityDef, data, effectiveTenantId, ct, evalContext);
            created = deepResult.RootEntity;
        }
        else if (entityDef.ParentEntity != null)
        {
            _logger.LogDebug("Inheritance insert for {Entity} (parent: {Parent})", entity, entityDef.ParentEntity.Name);
            var insertQueries = _sqlBuilder.BuildInheritanceInsertQueries(entityDef, data, effectiveTenantId);
            var partialResult = await _queryExecutor.ExecuteTemporalUpdateAsync(insertQueries, ct);
            // Re-read with JOIN to get merged parent+child fields
            var insertedId = ExtractEntityId(partialResult!);
            created = await FetchCurrentRecordAsync(entityDef, effectiveTenantId, insertedId!.Value, ct);
        }
        else
        {
            var (sql, parameters) = _sqlBuilder.BuildInsertQuery(entityDef, data, effectiveTenantId, context.UserId);
            created = await _queryExecutor.ExecuteReturningAsync(sql, parameters, ct);
        }

        if (created == null)
            throw new InvalidOperationException("Insert did not return the created entity");

        var createdId = ExtractEntityId(created);

        // Store localized field translations
        if (createdId != null)
            await StoreLocalizedFieldsAsync(entityDef, createdId.Value, data, context.Locale, effectiveTenantId, ct);

        // Enqueue domain event (dispatched post-commit by UoW)
        EnqueueDomainEvent($"{entity}Created", entity, createdId, created, effectiveTenantId, context.UserId, module);

        // Execute "after create" rules (non-critical, best-effort)
        try { await _ruleEngine.ExecuteAfterCreateAsync(entityDef, created, evalContext); }
        catch (Exception ex) { _logger.LogWarning(ex, "After-create rules failed for {Entity}", entity); }

        _logger.LogInformation("Created {Module}.{Entity} with ID {Id} for tenant {TenantId}",
            module, entity, createdId, context.TenantId);

        var createResult = EntityOperationResult.Success(created, 201);
        SurfaceRuleMessages(ruleResult, createResult);
        return createResult;
    }

    /// <summary>
    /// Update an existing entity (PATCH). Performs validation, rule execution, update, event enqueue.
    /// Caller must manage UnitOfWork lifecycle.
    /// </summary>
    public async Task<EntityOperationResult> UpdateAsync(
        BmEntity entityDef, string module, string entity, Guid id,
        Dictionary<string, object?> data,
        RequestContext context,
        string? ifMatch = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Updating {Module}.{Entity} with ID {Id} for tenant {TenantId}",
            module, entity, id, context.TenantId);

        var effectiveTenantId = context.GetEffectiveTenantId(entityDef);

        // Get current record for rule evaluation
        var currentRecord = await FetchCurrentRecordAsync(entityDef, effectiveTenantId, id, ct);
        if (currentRecord == null)
            return EntityOperationResult.NotFound($"{module}.{entity}", id);

        // Validate If-Match ETag
        var etagError = ValidateETag(currentRecord, ifMatch, module, entity, id);
        if (etagError != null)
            return etagError;

        // Validate input data (strip computed, enums, JSONB, HasStream, FK — no required assoc check for PATCH)
        var validationError = await ValidateInputData(entityDef, data, effectiveTenantId,
            isUpdate: true, validateRequiredAssociations: false, ct);
        if (validationError != null)
            return validationError;

        // Execute "before update" rules
        var evalContext = context.ToEvaluationContext();
        var ruleResult = await _ruleEngine.ExecuteBeforeUpdateAsync(entityDef, currentRecord, data, evalContext);

        if (!ruleResult.Success)
        {
            _logger.LogWarning("Update validation failed for {Entity}: {ErrorCount} errors",
                entity, ruleResult.Errors.Count);
            return EntityOperationResult.Error("ValidationFailed",
                string.Join("; ", ruleResult.Errors.Select(e => e.Message)));
        }

        // Apply computed values from rules
        foreach (var (field, value) in ruleResult.ComputedValues)
            data[field] = value;

        Dictionary<string, object?>? updated;

        // Deep update, temporal, inheritance, or standard update
        if (_deepUpdateHandler.HasNestedObjects(entityDef, data))
        {
            _logger.LogInformation("Deep update detected for {Module}.{Entity} ID {Id}", module, entity, id);
            var deepResult = await _deepUpdateHandler.ExecuteAsync(entityDef, id, data, currentRecord, effectiveTenantId, ct, evalContext);
            updated = deepResult.RootEntity;
        }
        else if (entityDef.IsTemporal && entityDef.TemporalStrategy == TemporalStrategy.InlineHistory)
        {
            _logger.LogDebug("Entity {Entity} is temporal (InlineHistory), using temporal update", entity);

            foreach (var kvp in data)
                currentRecord[kvp.Key] = kvp.Value;
            if (currentRecord.ContainsKey("UpdatedAt"))
                currentRecord["UpdatedAt"] = DateTime.UtcNow;

            var closeStatements = _sqlBuilder.BuildTemporalUpdateStatements(entityDef, id, new Dictionary<string, object?>(), effectiveTenantId);
            if (closeStatements.Count > 0)
            {
                var (closeSql, closeParams) = closeStatements[0];
                await _queryExecutor.ExecuteReturningAsync(closeSql, closeParams, ct);
            }

            currentRecord.Remove("system_start");
            currentRecord.Remove("SystemStart");
            currentRecord.Remove("system_end");
            currentRecord.Remove("SystemEnd");
            currentRecord.Remove("created_at");
            currentRecord.Remove("updated_at");

            // Strip computed/virtual fields — they are GENERATED ALWAYS in PostgreSQL
            var computedFieldNames = entityDef.Fields
                .Where(f => f.IsComputed || f.IsVirtual)
                .Select(f => f.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in currentRecord.Keys.ToList())
            {
                if (computedFieldNames.Contains(key))
                    currentRecord.Remove(key);
            }

            var (insertSql, insertParams) = _sqlBuilder.BuildInsertQuery(entityDef, currentRecord, effectiveTenantId, context.UserId);
            updated = await _queryExecutor.ExecuteReturningAsync(insertSql, insertParams, ct);
        }
        else if (entityDef.IsTemporal)
        {
            var (sql, parameters) = _sqlBuilder.BuildUpdateQuery(entityDef, id, data, effectiveTenantId);
            updated = await _queryExecutor.ExecuteReturningAsync(sql, parameters, ct);
        }
        else if (entityDef.ParentEntity != null)
        {
            _logger.LogDebug("Inheritance update for {Entity} (parent: {Parent})", entity, entityDef.ParentEntity.Name);
            var updateQueries = _sqlBuilder.BuildInheritanceUpdateQueries(entityDef, id, data, effectiveTenantId);
            await _queryExecutor.ExecuteTemporalUpdateAsync(updateQueries, ct);
            // Re-read with JOIN to get merged parent+child fields
            updated = await FetchCurrentRecordAsync(entityDef, effectiveTenantId, id, ct);
        }
        else
        {
            var (sql, parameters) = _sqlBuilder.BuildUpdateQuery(entityDef, id, data, effectiveTenantId);
            updated = await _queryExecutor.ExecuteReturningAsync(sql, parameters, ct);
        }

        if (updated == null)
            return EntityOperationResult.NotFound($"{module}.{entity}", id);

        // Store localized field translations
        await StoreLocalizedFieldsAsync(entityDef, id, data, context.Locale, effectiveTenantId, ct);

        // Enqueue domain event
        EnqueueDomainEvent($"{entity}Updated", entity, id, updated, effectiveTenantId, context.UserId, module);

        // Execute "after update" rules (non-critical, best-effort)
        try { await _ruleEngine.ExecuteAfterUpdateAsync(entityDef, currentRecord, updated, evalContext); }
        catch (Exception ex) { _logger.LogWarning(ex, "After-update rules failed for {Entity}", entity); }

        _logger.LogInformation("Updated {Module}.{Entity} with ID {Id} for tenant {TenantId}",
            module, entity, id, context.TenantId);

        var updateResult = EntityOperationResult.Success(updated);
        SurfaceRuleMessages(ruleResult, updateResult);
        return updateResult;
    }

    /// <summary>
    /// Full replace of an entity (PUT). Fills missing fields with defaults.
    /// Caller must manage UnitOfWork lifecycle.
    /// </summary>
    public async Task<EntityOperationResult> ReplaceAsync(
        BmEntity entityDef, string module, string entity, Guid id,
        Dictionary<string, object?> data,
        RequestContext context,
        string? ifMatch = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("PUT replace {Module}.{Entity} with ID {Id} for tenant {TenantId}",
            module, entity, id, context.TenantId);

        var effectiveTenantId = context.GetEffectiveTenantId(entityDef);

        // Get current record
        var currentRecord = await FetchCurrentRecordAsync(entityDef, effectiveTenantId, id, ct);
        if (currentRecord == null)
            return EntityOperationResult.NotFound($"{module}.{entity}", id);

        // Validate If-Match ETag
        var etagError = ValidateETag(currentRecord, ifMatch, module, entity, id);
        if (etagError != null)
            return etagError;

        // HasStream: Strip media columns from input before building full data
        if (entityDef.HasStream)
        {
            data.Remove("MediaContent");
            data.Remove("MediaContentType");
            data.Remove("MediaEtag");
        }

        // Build complete data with defaults for missing fields
        var fullData = new Dictionary<string, object?>();
        foreach (var field in entityDef.Fields)
        {
            var fieldName = field.Name;
            var snakeCaseName = NamingConvention.ToSnakeCase(fieldName);

            if (fieldName.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Equals("tenantId", StringComparison.OrdinalIgnoreCase) ||
                field.IsComputed || field.IsReadonly || field.IsImmutable)
                continue;

            var providedValue = data.Keys
                .FirstOrDefault(k => k.Equals(fieldName, StringComparison.OrdinalIgnoreCase) ||
                                     k.Equals(snakeCaseName, StringComparison.OrdinalIgnoreCase));

            if (providedValue != null)
            {
                fullData[fieldName] = data[providedValue];
            }
            else if (field.IsComputed || field.IsReadonly || field.IsImmutable)
            {
                continue;
            }
            else
            {
                fullData[fieldName] = field.DefaultValueString ?? null;
            }
        }

        // Validate full data (strip computed, enums, JSONB, HasStream, associations, FK)
        var validationError = await ValidateInputData(entityDef, fullData, effectiveTenantId,
            isUpdate: true, validateRequiredAssociations: true, ct);
        if (validationError != null)
            return validationError;

        // Execute "before update" rules
        var evalContext = context.ToEvaluationContext();
        var ruleResult = await _ruleEngine.ExecuteBeforeUpdateAsync(entityDef, currentRecord, fullData, evalContext);

        if (!ruleResult.Success)
        {
            _logger.LogWarning("PUT validation failed for {Entity}: {ErrorCount} errors",
                entity, ruleResult.Errors.Count);
            return EntityOperationResult.Error("ValidationFailed",
                string.Join("; ", ruleResult.Errors.Select(e => e.Message)));
        }

        // Apply computed values from rules
        foreach (var (field, value) in ruleResult.ComputedValues)
            fullData[field] = value;

        // Execute update
        var (sql, parameters) = _sqlBuilder.BuildUpdateQuery(entityDef, id, fullData, effectiveTenantId);
        var updated = await _queryExecutor.ExecuteReturningAsync(sql, parameters, ct);

        if (updated == null)
            return EntityOperationResult.NotFound($"{module}.{entity}", id);

        // Enqueue domain event
        EnqueueDomainEvent($"{entity}Updated", entity, id, updated, effectiveTenantId, context.UserId, module);

        // Execute "after update" rules (non-critical, best-effort)
        try { await _ruleEngine.ExecuteAfterUpdateAsync(entityDef, currentRecord, updated, evalContext); }
        catch (Exception ex) { _logger.LogWarning(ex, "After-update rules failed for PUT {Entity}", entity); }

        _logger.LogInformation("PUT replaced {Module}.{Entity} with ID {Id} for tenant {TenantId}",
            module, entity, id, context.TenantId);

        var replaceResult = EntityOperationResult.Success(updated);
        SurfaceRuleMessages(ruleResult, replaceResult);
        return replaceResult;
    }

    /// <summary>
    /// Delete an entity. Performs referential integrity checks, rule execution, cascade, delete.
    /// Caller must manage UnitOfWork lifecycle.
    /// </summary>
    public async Task<EntityOperationResult> DeleteAsync(
        BmEntity entityDef, string module, string entity, Guid id,
        RequestContext context,
        bool soft = false,
        string? ifMatch = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Deleting {Module}.{Entity} with ID {Id} for tenant {TenantId}. Soft: {Soft}",
            module, entity, id, context.TenantId, soft);

        var effectiveTenantId = context.GetEffectiveTenantId(entityDef);

        // Get current record for rule evaluation
        var existingData = await FetchCurrentRecordAsync(entityDef, effectiveTenantId, id, ct);
        if (existingData == null)
            return EntityOperationResult.NotFound($"{module}.{entity}", id);

        // Validate If-Match ETag
        var etagError = ValidateETag(existingData, ifMatch, module, entity, id);
        if (etagError != null)
            return etagError;

        // Check referential integrity (RESTRICT associations)
        var refErrors = await _refIntegrity.CheckDeleteConstraintsAsync(entityDef, id, effectiveTenantId, ct);
        if (refErrors.Count > 0)
            return EntityOperationResult.Conflict(string.Join("; ", refErrors));

        // Execute "before delete" rules
        var evalContext = context.ToEvaluationContext();
        var ruleResult = await _ruleEngine.ExecuteBeforeDeleteAsync(entityDef, existingData, evalContext);

        if (!ruleResult.Success)
        {
            _logger.LogWarning("Delete validation failed for {Entity}: {ErrorCount} errors",
                entity, ruleResult.Errors.Count);
            return EntityOperationResult.Error("ValidationFailed",
                string.Join("; ", ruleResult.Errors.Select(e => e.Message)));
        }

        // Cascade delete compositions, junction rows, texts
        await _refIntegrity.CascadeDeleteAsync(entityDef, id, effectiveTenantId, soft, ct);

        // Build and execute DELETE
        if (entityDef.ParentEntity != null)
        {
            _logger.LogDebug("Inheritance delete for {Entity} (parent: {Parent})", entity, entityDef.ParentEntity.Name);
            var deleteQueries = _sqlBuilder.BuildInheritanceDeleteQueries(entityDef, id, effectiveTenantId, soft);
            await _queryExecutor.ExecuteTemporalUpdateAsync(deleteQueries, ct);
        }
        else
        {
            var (sql, parameters) = _sqlBuilder.BuildDeleteQuery(entityDef, id, effectiveTenantId, soft);
            await _queryExecutor.ExecuteNonQueryAsync(sql, parameters, ct);
        }

        // Enqueue domain event
        EnqueueDomainEvent($"{entity}Deleted", entity, id, null, effectiveTenantId, context.UserId, module);

        // Execute "after delete" rules (non-critical, best-effort)
        try { await _ruleEngine.ExecuteAfterDeleteAsync(entityDef, existingData, evalContext); }
        catch (Exception ex) { _logger.LogWarning(ex, "After-delete rules failed for {Entity}", entity); }

        _logger.LogInformation("Deleted {Module}.{Entity} with ID {Id} for tenant {TenantId}",
            module, entity, id, context.TenantId);

        return EntityOperationResult.Deleted();
    }

    // ────────────────────────── Private helpers ──────────────────────────

    /// <summary>
    /// Fetch a single entity record using inheritance-aware query if needed.
    /// </summary>
    private async Task<Dictionary<string, object?>?> FetchCurrentRecordAsync(
        BmEntity entityDef, Guid? effectiveTenantId, Guid id, CancellationToken ct)
    {
        var options = new QueryOptions { TenantId = effectiveTenantId };
        var (sql, parameters) = BuildInheritanceAwareSelectQuery(entityDef, options, id);
        return await _queryExecutor.ExecuteSingleAsync(sql, parameters, ct);
    }

    /// <summary>
    /// Validate ETag precondition. Returns an error result on mismatch, or null if valid/not provided.
    /// </summary>
    private EntityOperationResult? ValidateETag(
        Dictionary<string, object?> currentRecord, string? ifMatch,
        string module, string entity, Guid id)
    {
        if (string.IsNullOrEmpty(ifMatch))
            return null;

        var currentETag = ETagGenerator.Generate(currentRecord);
        if (!ETagGenerator.Matches(ifMatch, currentETag))
        {
            _logger.LogWarning("ETag mismatch for {Module}.{Entity} ID {Id}", module, entity, id);
            return EntityOperationResult.PreconditionFailed(ETagGenerator.GenerateWeakETag(currentRecord));
        }

        return null;
    }

    /// <summary>
    /// Validate input data: strip computed fields, validate enums/JSONB, strip HasStream columns,
    /// optionally validate required associations, and validate FK targets exist.
    /// Returns null on success, or an error result.
    /// </summary>
    private async Task<EntityOperationResult?> ValidateInputData(
        BmEntity entityDef, Dictionary<string, object?> data, Guid? effectiveTenantId,
        bool isUpdate, bool validateRequiredAssociations, CancellationToken ct)
    {
        // Strip computed/virtual fields (and readonly/immutable if update)
        var stripped = EntityValidationService.StripComputedFields(entityDef, data, isUpdate: isUpdate);
        if (stripped.Count > 0)
            _logger.LogDebug("Stripped protected fields: {Fields}", string.Join(", ", stripped));

        // Validate enum fields
        var enumError = await _validationService.ValidateEnumFieldsAsync(entityDef, data);
        if (enumError != null)
            return EntityOperationResult.Error("InvalidEnumValue", enumError);

        // Validate JSONB structured type fields
        var jsonbError = await _validationService.ValidateJsonbFieldsAsync(entityDef, data);
        if (jsonbError != null)
            return EntityOperationResult.Error("InvalidJsonbStructure", jsonbError);

        // HasStream: Strip media columns — clients cannot set these directly
        if (entityDef.HasStream)
        {
            data.Remove("MediaContent");
            data.Remove("MediaContentType");
            data.Remove("MediaEtag");
        }

        // Validate required associations
        if (validateRequiredAssociations)
        {
            var assocErrors = EntityValidationService.ValidateRequiredAssociations(entityDef, data);
            if (assocErrors.Count > 0)
                return EntityOperationResult.Error("CardinalityViolation", string.Join("; ", assocErrors));
        }

        // Validate FK targets exist
        var fkErrors = await _refIntegrity.ValidateForeignKeysAsync(entityDef, data, effectiveTenantId, ct);
        if (fkErrors.Count > 0)
            return EntityOperationResult.Error("ForeignKeyViolation", string.Join("; ", fkErrors));

        return null;
    }

    /// <summary>
    /// Store localized field translations for an entity if locale is set and entity has localized fields.
    /// </summary>
    private async Task StoreLocalizedFieldsAsync(
        BmEntity entityDef, Guid id, Dictionary<string, object?> data,
        string? locale, Guid? effectiveTenantId, CancellationToken ct)
    {
        if (locale == null || !_sqlBuilder.HasLocalizedFields(entityDef))
            return;

        var localizedFieldNames = _sqlBuilder.GetLocalizedFieldNames(entityDef);
        var localizedData = data
            .Where(kvp => localizedFieldNames.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        if (localizedData.Count > 0)
        {
            var (textsSql, textsParams) = _sqlBuilder.BuildTextsUpsertQuery(
                entityDef, id, locale, localizedData, effectiveTenantId);
            await _queryExecutor.ExecuteNonQueryAsync(textsSql, textsParams, ct);
        }
    }

    /// <summary>
    /// Enqueue a domain event to be dispatched post-commit by the UnitOfWork.
    /// </summary>
    private void EnqueueDomainEvent(
        string eventName, string entity, Guid? entityId,
        Dictionary<string, object?>? payload, Guid? effectiveTenantId,
        Guid? userId, string module)
    {
        _unitOfWork.EnqueueEvent(new DomainEvent
        {
            EventName = eventName,
            EntityName = entity,
            EntityId = entityId,
            Payload = payload != null ? new Dictionary<string, object?>(payload) : null,
            TenantId = effectiveTenantId,
            UserId = userId,
            SourceModule = module
        });
    }

    /// <summary>
    /// Extract entity ID (Guid) from a record dictionary, checking both "Id" and "id" keys.
    /// </summary>
    private static Guid? ExtractEntityId(Dictionary<string, object?> record)
    {
        var idValue = record.GetValueOrDefault("Id") ?? record.GetValueOrDefault("id");
        if (idValue == null) return null;
        return idValue is Guid g ? g : Guid.Parse(idValue.ToString()!);
    }

    private (string Sql, IReadOnlyList<Npgsql.NpgsqlParameter> Parameters) BuildInheritanceAwareSelectQuery(
        BmEntity entityDef, QueryOptions options, Guid? id = null)
    {
        if (entityDef.ParentEntity != null)
            return _sqlBuilder.BuildInheritanceSelectQuery(entityDef, options, id);
        return _sqlBuilder.BuildSelectQuery(entityDef, options, id);
    }

    /// <summary>
    /// Copy non-blocking warnings and infos from rule execution result to the operation result.
    /// </summary>
    private static void SurfaceRuleMessages(RuleExecutionResult ruleResult, EntityOperationResult opResult)
    {
        foreach (var w in ruleResult.Warnings)
            opResult.Warnings.Add(w.Message);
        foreach (var i in ruleResult.Infos)
            opResult.Infos.Add(i.Message);
    }
}
