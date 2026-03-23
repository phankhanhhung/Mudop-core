using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Registry.Entities.Normalized;
using Microsoft.EntityFrameworkCore;

namespace BMMDL.Registry.Repositories.Persistence;

/// <summary>
/// Handles persistence of BmMigrationDef objects.
/// </summary>
internal sealed class MigrationPersister
{
    private readonly RepositoryContext _ctx;

    public MigrationPersister(RepositoryContext ctx)
    {
        _ctx = ctx;
    }

    public async Task SaveMigrationDefAsync(BmMigrationDef migration, CancellationToken ct = default)
    {
        var ns = await _ctx.GetOrCreateNamespaceAsync(migration.Namespace, ct);
        var existing = await _ctx.Db.MigrationDefs.Include(m => m.Steps)
            .FirstOrDefaultAsync(m => m.TenantId == _ctx.TenantId && m.QualifiedName == migration.QualifiedName, ct);

        MigrationDefRecord record;
        if (existing != null)
        {
            record = existing;
            record.Name = migration.Name;
            record.Version = migration.Version;
            record.Author = migration.Author;
            record.Description = migration.Description;
            record.Breaking = migration.Breaking;
            record.DependenciesJson = migration.Dependencies.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(migration.Dependencies)
                : null;
            record.NamespaceId = ns?.Id;
            record.ModuleId = _ctx.ModuleId ?? record.ModuleId;
            record.StartLine = migration.StartLine;
            record.EndLine = migration.EndLine;

            _ctx.Db.MigrationSteps.RemoveRange(record.Steps);
        }
        else
        {
            record = new MigrationDefRecord
            {
                Id = Guid.NewGuid(),
                TenantId = _ctx.TenantId,
                ModuleId = _ctx.ModuleId,
                NamespaceId = ns?.Id,
                Name = migration.Name,
                QualifiedName = migration.QualifiedName,
                Version = migration.Version,
                Author = migration.Author,
                Description = migration.Description,
                Breaking = migration.Breaking,
                DependenciesJson = migration.Dependencies.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(migration.Dependencies)
                    : null,
                StartLine = migration.StartLine,
                EndLine = migration.EndLine,
                CreatedAt = DateTime.UtcNow
            };
            _ctx.Db.MigrationDefs.Add(record);
        }

        int pos = 0;
        foreach (var step in migration.UpSteps)
        {
            record.Steps.Add(new MigrationStepRecord
            {
                Id = Guid.NewGuid(),
                MigrationDefId = record.Id,
                Direction = "up",
                StepType = GetStepType(step),
                StepJson = SerializeMigrationStep(step),
                Position = pos++
            });
        }

        pos = 0;
        foreach (var step in migration.DownSteps)
        {
            record.Steps.Add(new MigrationStepRecord
            {
                Id = Guid.NewGuid(),
                MigrationDefId = record.Id,
                Direction = "down",
                StepType = GetStepType(step),
                StepJson = SerializeMigrationStep(step),
                Position = pos++
            });
        }

        foreach (var annotation in migration.Annotations)
        {
            string? value = null;
            if (annotation.Properties?.Count > 0)
                value = System.Text.Json.JsonSerializer.Serialize(annotation.Properties);
            else if (annotation.Value != null)
                value = System.Text.Json.JsonSerializer.Serialize(annotation.Value);

            _ctx.Db.NormalizedAnnotations.Add(new NormalizedAnnotation
            {
                Id = Guid.NewGuid(),
                OwnerType = "migration_def",
                OwnerId = record.Id,
                Name = annotation.Name,
                Value = value
            });
        }

        await _ctx.Db.SaveChangesAsync(ct);
    }

    public async Task SaveMigrationDefsAsync(IEnumerable<BmMigrationDef> migrations, CancellationToken ct = default)
    {
        foreach (var m in migrations) await SaveMigrationDefAsync(m, ct);
    }

    public async Task<List<MigrationDefRecord>> LoadMigrationDefsAsync(CancellationToken ct)
    {
        return await _ctx.Db.MigrationDefs
            .AsNoTracking()
            .Where(m => m.TenantId == _ctx.TenantId)
            .Include(m => m.Steps)
            .Include(m => m.Namespace)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    public BmMigrationDef MapToBmMigrationDef(MigrationDefRecord record)
    {
        var migration = new BmMigrationDef
        {
            Name = record.Name,
            Namespace = record.Namespace?.Name ?? "",
            Version = record.Version,
            Author = record.Author,
            Description = record.Description,
            Breaking = record.Breaking,
            StartLine = record.StartLine ?? 0,
            EndLine = record.EndLine ?? 0
        };

        if (!string.IsNullOrEmpty(record.DependenciesJson))
        {
            var deps = System.Text.Json.JsonSerializer.Deserialize<List<string>>(record.DependenciesJson);
            if (deps != null)
                migration.Dependencies.AddRange(deps);
        }

        foreach (var step in record.Steps.Where(s => s.Direction == "up").OrderBy(s => s.Position))
        {
            var bmStep = DeserializeMigrationStep(step);
            if (bmStep != null)
                migration.UpSteps.Add(bmStep);
        }

        foreach (var step in record.Steps.Where(s => s.Direction == "down").OrderBy(s => s.Position))
        {
            var bmStep = DeserializeMigrationStep(step);
            if (bmStep != null)
                migration.DownSteps.Add(bmStep);
        }

        return migration;
    }

    private static string GetStepType(BmMigrationStep step) => step switch
    {
        BmAlterEntityStep => "alter_entity",
        BmAddEntityStep => "add_entity",
        BmDropEntityStep => "drop_entity",
        BmTransformStep => "transform",
        _ => "unknown"
    };

    private static string SerializeMigrationStep(BmMigrationStep step)
    {
        return System.Text.Json.JsonSerializer.Serialize(step, step.GetType(), new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static BmMigrationStep? DeserializeMigrationStep(MigrationStepRecord record)
    {
        try
        {
            return record.StepType switch
            {
                "alter_entity" => System.Text.Json.JsonSerializer.Deserialize<BmAlterEntityStep>(record.StepJson),
                "add_entity" => System.Text.Json.JsonSerializer.Deserialize<BmAddEntityStep>(record.StepJson),
                "drop_entity" => System.Text.Json.JsonSerializer.Deserialize<BmDropEntityStep>(record.StepJson),
                "transform" => System.Text.Json.JsonSerializer.Deserialize<BmTransformStep>(record.StepJson),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}
