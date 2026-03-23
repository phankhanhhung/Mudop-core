using BMMDL.Registry.Data;
using BMMDL.Registry.Entities.Normalized;
using BMMDL.Registry.Repositories.Serialization;
using Microsoft.EntityFrameworkCore;

namespace BMMDL.Registry.Repositories.Persistence;

/// <summary>
/// Shared dependencies for all persisters. Created once per repository instance.
/// </summary>
internal sealed class RepositoryContext
{
    public readonly RegistryDbContext Db;
    public readonly Guid TenantId;
    public readonly Guid? ModuleId;
    public readonly Dictionary<string, Namespace> NamespaceCache;
    public readonly ExpressionAstSerializer ExprSerializer;
    public readonly StatementAstSerializer StmtSerializer;

    public RepositoryContext(RegistryDbContext db, Guid tenantId, Guid? moduleId)
    {
        Db = db;
        TenantId = tenantId;
        ModuleId = moduleId;
        NamespaceCache = new Dictionary<string, Namespace>();
        ExprSerializer = new ExpressionAstSerializer(db);
        StmtSerializer = new StatementAstSerializer(db, ExprSerializer);
    }

    public async Task<Namespace?> GetOrCreateNamespaceAsync(string? name, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(name)) return null;

        if (NamespaceCache.TryGetValue(name, out var cached))
            return cached;

        var ns = await Db.Namespaces.FirstOrDefaultAsync(n => n.TenantId == TenantId && n.Name == name, ct);
        if (ns == null)
        {
            ns = new Namespace { Id = Guid.NewGuid(), TenantId = TenantId, Name = name };
            Db.Namespaces.Add(ns);
            await Db.SaveChangesAsync(ct);
        }

        NamespaceCache[name] = ns;
        return ns;
    }
}
