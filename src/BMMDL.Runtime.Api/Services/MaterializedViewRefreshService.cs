namespace BMMDL.Runtime.Api.Services;

using System.Collections.Concurrent;
using BMMDL.MetaModel;
using BMMDL.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

/// <summary>
/// Background service that periodically refreshes materialized views.
/// Views with @RefreshInterval annotation are refreshed at configured intervals.
/// </summary>
public class MaterializedViewRefreshService : BackgroundService
{
    private readonly MetaModelCacheManager _cacheManager;  // Changed from MetaModelCache
    private readonly IConfiguration _configuration;
    private readonly ILogger<MaterializedViewRefreshService> _logger;
    private readonly TimeSpan _defaultInterval = TimeSpan.FromHours(1);
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

    public MaterializedViewRefreshService(
        MetaModelCacheManager cacheManager,  // Changed from MetaModelCache
        IConfiguration configuration,
        ILogger<MaterializedViewRefreshService> logger)
    {
        _cacheManager = cacheManager;  // Store manager, don't access .Cache yet
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MaterializedViewRefreshService started");

        // Wait for app startup
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshDueViews(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in materialized view refresh cycle");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task RefreshDueViews(CancellationToken ct)
    {
        var cache = await _cacheManager.GetCacheAsync(ct);
        var materializedViews = cache.Views
            .Where(v => v.HasAnnotation("Materialized"))
            .Where(v => v.HasAnnotation("RefreshInterval") || v.HasAnnotation("AutoRefresh"))
            .ToList();

        if (materializedViews.Count == 0)
            return;

        _logger.LogDebug("Checking {Count} materialized views for refresh", materializedViews.Count);

        var connectionString = _configuration.GetConnectionString("TenantDb")
            ?? _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrEmpty(connectionString))
        {
            _logger.LogWarning("No connection string for materialized view refresh");
            return;
        }

        foreach (var view in materializedViews)
        {
            try
            {
                var interval = ParseRefreshInterval(view);
                var lastRefresh = GetLastRefreshTime(view.QualifiedName);
                
                if (DateTime.UtcNow - lastRefresh < interval)
                    continue;

                await RefreshView(view, connectionString, ct);
                RecordRefreshTime(view.QualifiedName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh view {ViewName}", view.QualifiedName);
            }
        }
    }

    private async Task RefreshView(BmView view, string connectionString, CancellationToken ct)
    {
        var schemaName = BMMDL.MetaModel.Utilities.NamingConvention.GetSchemaName(view.Namespace ?? "");
        var sqlViewName = BMMDL.MetaModel.Utilities.NamingConvention.GetColumnName(view.Name);
        var qualifiedViewName = $"{schemaName}.{sqlViewName}";

        var concurrent = view.HasAnnotation("ConcurrentRefresh");
        var refreshSql = concurrent
            ? $"REFRESH MATERIALIZED VIEW CONCURRENTLY {qualifiedViewName}"
            : $"REFRESH MATERIALIZED VIEW {qualifiedViewName}";

        _logger.LogInformation("Refreshing materialized view {ViewName}", view.QualifiedName);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = refreshSql;
        cmd.CommandTimeout = 600; // 10 minutes for large views
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Refreshed materialized view {ViewName}", view.QualifiedName);
    }

    private TimeSpan ParseRefreshInterval(BmView view)
    {
        var intervalStr = view.GetAnnotation("RefreshInterval")?.Value as string;
        if (string.IsNullOrEmpty(intervalStr))
            return _defaultInterval;

        // Parse formats: "1h", "30m", "1d"
        var value = intervalStr.TrimEnd('h', 'm', 'd', 's');
        if (!int.TryParse(value, out var number))
            return _defaultInterval;

        return intervalStr.Last() switch
        {
            'h' => TimeSpan.FromHours(number),
            'm' => TimeSpan.FromMinutes(number),
            'd' => TimeSpan.FromDays(number),
            's' => TimeSpan.FromSeconds(number),
            _ => _defaultInterval
        };
    }

    // Simple in-memory tracking of last refresh times
    private static readonly ConcurrentDictionary<string, DateTime> _lastRefreshTimes = new();

    private static DateTime GetLastRefreshTime(string viewName)
    {
        return _lastRefreshTimes.TryGetValue(viewName, out var time) 
            ? time 
            : DateTime.MinValue;
    }

    private static void RecordRefreshTime(string viewName)
    {
        _lastRefreshTimes[viewName] = DateTime.UtcNow;
    }
}
