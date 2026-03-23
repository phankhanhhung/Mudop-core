using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.Registry.Entities.Normalized;
using BMMDL.Registry.Repositories.Serialization;
using Microsoft.EntityFrameworkCore;

namespace BMMDL.Registry.Repositories.Persistence;

/// <summary>
/// Handles persistence of BmService objects.
/// </summary>
internal sealed class ServicePersister
{
    private readonly RepositoryContext _ctx;

    public ServicePersister(RepositoryContext ctx)
    {
        _ctx = ctx;
    }

    public async Task SaveServiceAsync(BmService service, CancellationToken ct = default)
    {
        var ns = await _ctx.GetOrCreateNamespaceAsync(service.Namespace, ct);

        var existing = await _ctx.Db.Services
            .Include(s => s.Operations).ThenInclude(o => o.Parameters)
            .Include(s => s.ExposedEntities)
            .FirstOrDefaultAsync(s => s.QualifiedName == service.QualifiedName, ct);

        if (existing != null)
        {
            var existingOpIds = existing.Operations.ToDictionary(o => o.Name, o => o.Id);
            var existingParamIds = existing.Operations
                .SelectMany(o => o.Parameters.Select(p => (Key: $"{o.Name}:{p.Name}", p.Id)))
                .ToDictionary(x => x.Key, x => x.Id);

            existing.Name = service.Name;
            existing.NamespaceId = ns?.Id;
            existing.ForEntityName = service.ForEntity;
            existing.Operations.Clear();
            existing.ExposedEntities.Clear();
            MapServiceChildren(service, existing, existingOpIds, existingParamIds);
        }
        else
        {
            var record = new ServiceRecord
            {
                Id = Guid.NewGuid(),
                TenantId = _ctx.TenantId,
                Name = service.Name,
                QualifiedName = service.QualifiedName,
                NamespaceId = ns?.Id,
                ForEntityName = service.ForEntity
            };
            MapServiceChildren(service, record);
            _ctx.Db.Services.Add(record);
        }

        await _ctx.Db.SaveChangesAsync(ct);
    }

    public void MapServiceChildren(
        BmService service,
        ServiceRecord record,
        IReadOnlyDictionary<string, Guid>? existingOpIds = null,
        IReadOnlyDictionary<string, Guid>? existingParamIds = null)
    {
        int opPos = 0;
        foreach (var func in service.Functions)
        {
            var opId = existingOpIds?.GetValueOrDefault(func.Name) ?? Guid.NewGuid();
            var op = new ServiceOperation
            {
                Id = opId, ServiceId = record.Id, Name = func.Name,
                OperationType = "function", ReturnType = func.ReturnType, Position = opPos++
            };
            int pos = 0;
            foreach (var p in func.Parameters)
            {
                var paramId = existingParamIds?.GetValueOrDefault($"{func.Name}:{p.Name}") ?? Guid.NewGuid();
                var paramRecord = new OperationParameter
                {
                    Id = paramId, OperationId = op.Id, Name = p.Name,
                    TypeString = p.Type, DefaultValue = p.DefaultValueString, Position = pos++
                };
                if (p.DefaultValueAst != null)
                    paramRecord.DefaultValueExprRootId = _ctx.ExprSerializer.SaveExpressionNodes(p.DefaultValueAst, paramRecord.Id, "operation_parameter", "default_value");
                op.Parameters.Add(paramRecord);
            }
            if (func.Body.Count > 0)
            {
                op.BodyDefinitionHash = StatementAstSerializer.ComputeBodyHash(func.Body);
                op.BodyRootStatementId = _ctx.StmtSerializer.SaveStatementNodes(func.Body, op.Id, "service_operation");
            }
            record.Operations.Add(op);
        }

        foreach (var action in service.Actions)
        {
            var opId = existingOpIds?.GetValueOrDefault(action.Name) ?? Guid.NewGuid();
            var op = new ServiceOperation
            {
                Id = opId, ServiceId = record.Id, Name = action.Name,
                OperationType = "action", ReturnType = action.ReturnType, Position = opPos++
            };
            int pos = 0;
            foreach (var p in action.Parameters)
            {
                var paramId = existingParamIds?.GetValueOrDefault($"{action.Name}:{p.Name}") ?? Guid.NewGuid();
                var paramRecord = new OperationParameter
                {
                    Id = paramId, OperationId = op.Id, Name = p.Name,
                    TypeString = p.Type, DefaultValue = p.DefaultValueString, Position = pos++
                };
                if (p.DefaultValueAst != null)
                    paramRecord.DefaultValueExprRootId = _ctx.ExprSerializer.SaveExpressionNodes(p.DefaultValueAst, paramRecord.Id, "operation_parameter", "default_value");
                op.Parameters.Add(paramRecord);
            }

            int emitPos = 0;
            foreach (var eventName in action.Emits)
                op.Emits.Add(new ServiceOperationEmit { OperationId = op.Id, EventName = eventName, Position = emitPos++ });

            if (action.Body.Count > 0)
            {
                op.BodyDefinitionHash = StatementAstSerializer.ComputeBodyHash(action.Body);
                op.BodyRootStatementId = _ctx.StmtSerializer.SaveStatementNodes(action.Body, op.Id, "service_operation");
            }

            // Action contract
            if (action.Preconditions.Count > 0)
            {
                var rootIds = new List<Guid>();
                for (int i = 0; i < action.Preconditions.Count; i++)
                {
                    var rootId = _ctx.ExprSerializer.SaveExpressionNodes(action.Preconditions[i], op.Id, "service_operation", $"precondition_{i}");
                    if (rootId.HasValue) rootIds.Add(rootId.Value);
                }
                op.PreconditionExprIds = System.Text.Json.JsonSerializer.Serialize(rootIds);
            }
            if (action.Postconditions.Count > 0)
            {
                var rootIds = new List<Guid>();
                for (int i = 0; i < action.Postconditions.Count; i++)
                {
                    var rootId = _ctx.ExprSerializer.SaveExpressionNodes(action.Postconditions[i], op.Id, "service_operation", $"postcondition_{i}");
                    if (rootId.HasValue) rootIds.Add(rootId.Value);
                }
                op.PostconditionExprIds = System.Text.Json.JsonSerializer.Serialize(rootIds);
            }
            if (action.Modifies.Count > 0)
            {
                var entries = new List<Dictionary<string, object>>();
                for (int i = 0; i < action.Modifies.Count; i++)
                {
                    var (fieldName, expr) = action.Modifies[i];
                    var rootId = _ctx.ExprSerializer.SaveExpressionNodes(expr, op.Id, "service_operation", $"modifies_{i}");
                    entries.Add(new Dictionary<string, object>
                    {
                        ["FieldName"] = fieldName,
                        ["ExprRootId"] = rootId?.ToString() ?? ""
                    });
                }
                op.ModifiesJson = System.Text.Json.JsonSerializer.Serialize(entries);
            }

            record.Operations.Add(op);
        }

        foreach (var e in service.Entities)
        {
            var exposed = new ServiceExposedEntity { ServiceId = record.Id, EntityName = e.QualifiedName };
            if (e.IncludeFields is { Count: > 0 })
                exposed.IncludeFieldsJson = System.Text.Json.JsonSerializer.Serialize(e.IncludeFields);
            if (e.ExcludeFields is { Count: > 0 })
                exposed.ExcludeFieldsJson = System.Text.Json.JsonSerializer.Serialize(e.ExcludeFields);
            record.ExposedEntities.Add(exposed);
        }

        int ehPos = 0;
        foreach (var handler in service.EventHandlers)
        {
            var ehRecord = new ServiceEventHandler
            {
                Id = Guid.NewGuid(), ServiceId = record.Id,
                EventName = handler.EventName, Position = ehPos++, CreatedAt = DateTime.UtcNow
            };
            if (handler.Statements.Count > 0)
            {
                ehRecord.BodyDefinitionHash = StatementAstSerializer.ComputeBodyHash(handler.Statements);
                ehRecord.BodyRootStatementId = _ctx.StmtSerializer.SaveStatementNodes(handler.Statements, ehRecord.Id, "service_event_handler");
            }
            record.EventHandlers.Add(ehRecord);
        }
    }

    public async Task SaveServicesAsync(IEnumerable<BmService> services, CancellationToken ct = default)
    {
        foreach (var service in services) await SaveServiceAsync(service, ct);
    }

    public async Task<IReadOnlyList<ServiceRecord>> GetServicesAsync(string? ns = null, CancellationToken ct = default)
    {
        var query = _ctx.Db.Services
            .AsNoTracking()
            .Include(s => s.Operations).ThenInclude(o => o.Parameters)
            .AsSplitQuery();
        if (!string.IsNullOrEmpty(ns))
            query = query.Where(s => s.Namespace != null && s.Namespace.Name == ns);
        return await query.OrderBy(s => s.QualifiedName).ToListAsync(ct);
    }

    public async Task<List<ServiceRecord>> LoadServicesAsync(CancellationToken ct)
    {
        return await _ctx.Db.Services
            .AsNoTracking()
            .Where(s => s.TenantId == _ctx.TenantId)
            .Include(s => s.Operations).ThenInclude(o => o.Parameters)
            .Include(s => s.Operations).ThenInclude(o => o.Emits)
            .Include(s => s.ExposedEntities)
            .Include(s => s.EventHandlers)
            .Include(s => s.Namespace)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    public BmService MapToBmService(ServiceRecord record)
    {
        var service = new BmService
        {
            Name = record.Name,
            Namespace = record.Namespace?.Name ?? "",
            ForEntity = record.ForEntityName
        };

        foreach (var op in record.Operations)
        {
            if (op.OperationType == "function")
            {
                var func = new BmFunction { Name = op.Name, ReturnType = op.ReturnType ?? "" };
                foreach (var p in op.Parameters.OrderBy(x => x.Position))
                {
                    var param = new BmParameter { Name = p.Name, Type = p.TypeString, DefaultValueString = p.DefaultValue };
                    if (p.DefaultValueExprRootId.HasValue)
                    {
                        var exprNodes = _ctx.Db.ExpressionNodes.AsNoTracking()
                            .Where(e => e.OwnerType == "operation_parameter" && e.OwnerId == p.Id)
                            .ToList();
                        if (exprNodes.Count > 0)
                            param.DefaultValueAst = _ctx.ExprSerializer.ReconstructBmExpression(p.DefaultValueExprRootId.Value, exprNodes);
                    }
                    func.Parameters.Add(param);
                }
                if (op.BodyRootStatementId.HasValue)
                {
                    var bodyStatements = _ctx.StmtSerializer.LoadBodyStatements(op.Id, "service_operation");
                    foreach (var stmt in bodyStatements) func.Body.Add(stmt);
                }
                service.Functions.Add(func);
            }
            else
            {
                var action = new BmAction { Name = op.Name, ReturnType = op.ReturnType ?? "" };
                foreach (var p in op.Parameters.OrderBy(x => x.Position))
                {
                    var param = new BmParameter { Name = p.Name, Type = p.TypeString, DefaultValueString = p.DefaultValue };
                    if (p.DefaultValueExprRootId.HasValue)
                    {
                        var exprNodes = _ctx.Db.ExpressionNodes.AsNoTracking()
                            .Where(e => e.OwnerType == "operation_parameter" && e.OwnerId == p.Id)
                            .ToList();
                        if (exprNodes.Count > 0)
                            param.DefaultValueAst = _ctx.ExprSerializer.ReconstructBmExpression(p.DefaultValueExprRootId.Value, exprNodes);
                    }
                    action.Parameters.Add(param);
                }
                foreach (var emit in op.Emits.OrderBy(e => e.Position))
                    action.Emits.Add(emit.EventName);
                if (op.BodyRootStatementId.HasValue)
                {
                    var bodyStatements = _ctx.StmtSerializer.LoadBodyStatements(op.Id, "service_operation");
                    foreach (var stmt in bodyStatements) action.Body.Add(stmt);
                }

                // Reconstruct contracts
                if (!string.IsNullOrEmpty(op.PreconditionExprIds))
                {
                    var rootIds = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(op.PreconditionExprIds);
                    if (rootIds != null)
                    {
                        var opExprNodes = _ctx.Db.ExpressionNodes.AsNoTracking()
                            .Where(e => e.OwnerType == "service_operation" && e.OwnerId == op.Id
                                && e.OwnerField != null && e.OwnerField.StartsWith("precondition_"))
                            .ToList();
                        foreach (var rootId in rootIds)
                        {
                            var expr = _ctx.ExprSerializer.ReconstructBmExpression(rootId, opExprNodes);
                            if (expr != null) action.Preconditions.Add(expr);
                        }
                    }
                }
                if (!string.IsNullOrEmpty(op.PostconditionExprIds))
                {
                    var rootIds = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(op.PostconditionExprIds);
                    if (rootIds != null)
                    {
                        var opExprNodes = _ctx.Db.ExpressionNodes.AsNoTracking()
                            .Where(e => e.OwnerType == "service_operation" && e.OwnerId == op.Id
                                && e.OwnerField != null && e.OwnerField.StartsWith("postcondition_"))
                            .ToList();
                        foreach (var rootId in rootIds)
                        {
                            var expr = _ctx.ExprSerializer.ReconstructBmExpression(rootId, opExprNodes);
                            if (expr != null) action.Postconditions.Add(expr);
                        }
                    }
                }
                if (!string.IsNullOrEmpty(op.ModifiesJson))
                {
                    var entries = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(op.ModifiesJson);
                    if (entries != null)
                    {
                        var opExprNodes = _ctx.Db.ExpressionNodes.AsNoTracking()
                            .Where(e => e.OwnerType == "service_operation" && e.OwnerId == op.Id
                                && e.OwnerField != null && e.OwnerField.StartsWith("modifies_"))
                            .ToList();
                        foreach (var entry in entries)
                        {
                            var fieldName = entry["FieldName"].GetString() ?? "";
                            var exprRootIdStr = entry["ExprRootId"].GetString();
                            if (!string.IsNullOrEmpty(exprRootIdStr) && Guid.TryParse(exprRootIdStr, out var exprRootId))
                            {
                                var expr = _ctx.ExprSerializer.ReconstructBmExpression(exprRootId, opExprNodes);
                                if (expr != null) action.Modifies.Add((fieldName, expr));
                            }
                        }
                    }
                }

                service.Actions.Add(action);
            }
        }

        foreach (var exposed in record.ExposedEntities)
        {
            var parts = exposed.EntityName.Split('.');
            var entityName = parts.Length > 1 ? parts[^1] : exposed.EntityName;
            var entityNs = parts.Length > 1 ? string.Join(".", parts[..^1]) : "";
            var entity = new BmEntity { Name = entityName, Namespace = entityNs };
            entity.Aspects.Add(exposed.EntityName);
            if (!string.IsNullOrEmpty(exposed.IncludeFieldsJson))
                entity.IncludeFields = System.Text.Json.JsonSerializer.Deserialize<List<string>>(exposed.IncludeFieldsJson);
            if (!string.IsNullOrEmpty(exposed.ExcludeFieldsJson))
                entity.ExcludeFields = System.Text.Json.JsonSerializer.Deserialize<List<string>>(exposed.ExcludeFieldsJson);
            service.Entities.Add(entity);
        }

        foreach (var eh in record.EventHandlers.OrderBy(h => h.Position))
        {
            var handler = new BmEventHandler { EventName = eh.EventName };
            if (eh.BodyRootStatementId.HasValue)
            {
                var bodyStatements = _ctx.StmtSerializer.LoadBodyStatements(eh.Id, "service_event_handler");
                foreach (var stmt in bodyStatements) handler.Statements.Add(stmt);
            }
            service.EventHandlers.Add(handler);
        }

        return service;
    }
}
