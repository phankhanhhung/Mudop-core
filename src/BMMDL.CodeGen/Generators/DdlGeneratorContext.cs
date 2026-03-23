using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Registry.Services;

namespace BMMDL.CodeGen.Generators;

/// <summary>
/// Shared context for all DDL sub-generators.
/// Holds common dependencies and helper methods for table/column naming.
/// </summary>
internal class DdlGeneratorContext
{
    public MetaModelCache Cache { get; }
    public TypeResolver Resolver { get; }
    public BmModel? Model { get; }
    private readonly Dictionary<string, string> _entityToModuleMap;

    public DdlGeneratorContext(MetaModelCache cache, TypeResolver resolver, BmModel? model, Dictionary<string, string> entityToModuleMap)
    {
        Cache = cache;
        Resolver = resolver;
        Model = model;
        _entityToModuleMap = entityToModuleMap;
    }

    /// <summary>
    /// Get module name for an entity.
    /// </summary>
    public string? GetModuleNameForEntity(BmEntity entity)
    {
        if (_entityToModuleMap.TryGetValue(entity.QualifiedName, out var moduleName))
            return moduleName;
        return null;
    }

    /// <summary>
    /// Get fully qualified table name for an entity (schema.table).
    /// </summary>
    public string GetQualifiedTableNameForEntity(BmEntity entity)
    {
        var moduleName = GetModuleNameForEntity(entity);
        return NamingConvention.GetQualifiedTableName(entity, moduleName);
    }

    /// <summary>
    /// Get unqualified table name.
    /// </summary>
    public string GetUnqualifiedTableName(BmEntity entity)
    {
        return NamingConvention.GetTableName(entity);
    }

    /// <summary>
    /// Generate FK column definition for an association.
    /// </summary>
    public string GenerateForeignKeyColumn(BmAssociation assoc)
    {
        var columnName = NamingConvention.QuoteIdentifier(NamingConvention.GetFkColumnName(assoc.Name));
        var nullable = assoc.MinCardinality == 1 ? " NOT NULL" : "";
        return $"{columnName} UUID{nullable}";
    }
}
