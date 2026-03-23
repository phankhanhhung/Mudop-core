using BMMDL.MetaModel;

namespace BMMDL.Registry.Api.Services;

/// <summary>
/// Manages database schema operations: creation, migration, and teardown.
/// </summary>
public interface ISchemaManagementService
{
    string GetConnectionString();
    Task<string> InitSchemaFreshAsync(BmModel schemaModel, string connString);
    Task<string> MigrateSchemaAsync(BmModel schemaModel, string connString, string schemaName, List<string> warnings);
    Task DropSchemaIfExistsAsync(string connString, string schemaName);
    Task<bool> EnsureRegistrySchemaExistsAsync(List<string> messages);
}
