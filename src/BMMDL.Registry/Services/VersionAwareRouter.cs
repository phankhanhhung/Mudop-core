using BMMDL.MetaModel.Utilities;
using BMMDL.Registry.Entities;

namespace BMMDL.Registry.Services;

/// <summary>
/// Routes queries to correct table version based on upgrade status.
/// </summary>
public class VersionAwareRouter
{
    private readonly DualVersionSyncService _upgradeService;

    public VersionAwareRouter(DualVersionSyncService upgradeService)
    {
        _upgradeService = upgradeService;
    }

    /// <summary>
    /// Get table name for an entity, accounting for upgrade status.
    /// </summary>
    public async Task<TableRoutingResult> GetTableNameAsync(
        Guid tenantId,
        Guid moduleId,
        string schemaName,
        string entityName,
        QueryType queryType = QueryType.Read,
        CancellationToken ct = default)
    {
        var tableName = NamingConvention.ToSnakeCase(entityName);
        var baseTable = $"{schemaName}.{tableName}";
        
        var window = await _upgradeService.GetActiveUpgradeAsync(tenantId, moduleId, ct);
        
        if (window == null)
        {
            // Normal operation - single version
            return new TableRoutingResult
            {
                TableName = baseTable,
                IsInUpgrade = false,
                UseVersion = 1
            };
        }

        // During upgrade window
        return window.Status switch
        {
            UpgradeStatus.Preparing => new TableRoutingResult
            {
                // Still using v1 during preparation
                TableName = baseTable,
                IsInUpgrade = true,
                UseVersion = 1,
                UpgradePhase = window.Status
            },
            
            UpgradeStatus.DualVersion => queryType switch
            {
                QueryType.Write => new TableRoutingResult
                {
                    // Writes go to v2, sync trigger copies to v1
                    TableName = $"{schemaName}.{tableName}_v2",
                    IsInUpgrade = true,
                    UseVersion = 2,
                    UpgradePhase = window.Status,
                    Note = "Writes to v2 with sync trigger to v1"
                },
                _ => new TableRoutingResult
                {
                    // Reads from v1 for backward compat (or v2 if ready)
                    TableName = baseTable,
                    IsInUpgrade = true,
                    UseVersion = 1,
                    UpgradePhase = window.Status,
                    Note = "Reads from v1 during dual-version"
                }
            },
            
            UpgradeStatus.Cutover or UpgradeStatus.Validating => new TableRoutingResult
            {
                // All traffic to v2
                TableName = $"{schemaName}.{tableName}_v2",
                IsInUpgrade = true,
                UseVersion = 2,
                UpgradePhase = window.Status,
                Note = "v2 is primary during cutover"
            },
            
            UpgradeStatus.Completed => new TableRoutingResult
            {
                // Upgrade complete, using v2 as main
                // (Note: in real scenario, v2 would be renamed to base)
                TableName = baseTable,
                IsInUpgrade = false,
                UseVersion = 2
            },
            
            _ => new TableRoutingResult
            {
                TableName = baseTable,
                IsInUpgrade = false,
                UseVersion = 1
            }
        };
    }

    /// <summary>
    /// Check if entity should use v2 schema for new writes.
    /// </summary>
    public async Task<bool> ShouldUseV2SchemaAsync(
        Guid tenantId,
        Guid moduleId,
        CancellationToken ct = default)
    {
        var window = await _upgradeService.GetActiveUpgradeAsync(tenantId, moduleId, ct);
        if (window == null) return false;

        return window.Status is 
            UpgradeStatus.DualVersion or 
            UpgradeStatus.Cutover or 
            UpgradeStatus.Validating;
    }
}

#region Routing Types

public class TableRoutingResult
{
    public string TableName { get; set; } = "";
    public bool IsInUpgrade { get; set; }
    public int UseVersion { get; set; } = 1;
    public UpgradeStatus? UpgradePhase { get; set; }
    public string? Note { get; set; }
}

public enum QueryType
{
    Read,
    Write
}

#endregion
