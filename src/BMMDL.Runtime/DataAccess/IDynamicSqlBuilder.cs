namespace BMMDL.Runtime.DataAccess;

using BMMDL.MetaModel.Structure;
using Npgsql;

/// <summary>
/// Interface for parameterized SQL query generation from BMMDL entity metadata.
/// </summary>
public interface IDynamicSqlBuilder
{
    (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildSelectQuery(
        BmEntity entity, QueryOptions? options = null, Guid? id = null);

    (string Sql, IReadOnlyList<NpgsqlParameter> Parameters, List<string> ExpandedNavs) BuildSelectWithExpand(
        BmEntity entity, QueryOptions options, Guid? id = null);

    (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildInsertQuery(
        BmEntity entity, Dictionary<string, object?> data, Guid? tenantId = null, Guid? userId = null);

    (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildUpdateQuery(
        BmEntity entity, Guid id, Dictionary<string, object?> data, Guid? tenantId = null);

    (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildTextsUpsertQuery(
        BmEntity entity, Guid entityId, string locale, Dictionary<string, object?> localizedData, Guid? tenantId = null);

    bool HasLocalizedFields(BmEntity entity);

    HashSet<string> GetLocalizedFieldNames(BmEntity entity);

    List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> BuildTemporalUpdateStatements(
        BmEntity entity, Guid id, Dictionary<string, object?> data, Guid? tenantId = null);

    (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildDeleteQuery(
        BmEntity entity, Guid id, Guid? tenantId = null, bool softDelete = false);

    (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildInheritanceSelectQuery(
        BmEntity entity, QueryOptions? options = null, Guid? id = null);

    (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildPolymorphicSelectQuery(
        BmEntity parentEntity, List<BmEntity> derivedEntities, QueryOptions? options = null, Guid? id = null);

    List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> BuildInheritanceInsertQueries(
        BmEntity entity, Dictionary<string, object?> data, Guid? tenantId = null);

    List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> BuildInheritanceUpdateQueries(
        BmEntity entity, Guid id, Dictionary<string, object?> data, Guid? tenantId = null);

    List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> BuildInheritanceDeleteQueries(
        BmEntity entity, Guid id, Guid? tenantId = null, bool softDelete = false);

    (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildDeleteOrphansQuery(
        BmEntity childEntity, string fkColumnName, object parentId, IReadOnlyList<Guid> keepIds, Guid? tenantId = null);

    (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildCountQuery(
        BmEntity entity, QueryOptions? options = null);

    (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildExistsQuery(
        BmEntity entity, Guid id, Guid? tenantId = null);

    string GetTableName(BmEntity entity);

    (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildJunctionInsertQuery(
        BmEntity sourceEntity, BmEntity targetEntity, Guid sourceId, Guid targetId, Guid? tenantId = null);

    (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildJunctionDeleteQuery(
        BmEntity sourceEntity, BmEntity targetEntity, Guid sourceId, Guid targetId, Guid? tenantId = null);
}
