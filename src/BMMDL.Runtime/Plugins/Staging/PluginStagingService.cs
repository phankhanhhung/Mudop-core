using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Plugins.Loading;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BMMDL.Runtime.Plugins.Staging;

/// <summary>
/// Manages the plugin staging workflow: upload → validate → review → approve/reject.
/// Plugins are extracted to a staging directory and validated before being moved
/// to the live plugins directory.
/// </summary>
public sealed class PluginStagingService
{
    private readonly ITenantConnectionFactory _connectionFactory;
    private readonly PluginValidationPipeline _validationPipeline;
    private readonly PluginDirectoryLoader? _loader;
    private readonly ILogger<PluginStagingService> _logger;

    private static readonly string StagingRoot = Path.Combine("plugins", ".staging");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public PluginStagingService(
        ITenantConnectionFactory connectionFactory,
        PluginValidationPipeline validationPipeline,
        PluginDirectoryLoader? loader,
        ILogger<PluginStagingService> logger)
    {
        _connectionFactory = connectionFactory;
        _validationPipeline = validationPipeline;
        _loader = loader;
        _logger = logger;
    }

    /// <summary>
    /// Stage a plugin from a zip stream. Extracts to staging directory,
    /// runs validation, and persists a staging record.
    /// </summary>
    public async Task<PluginStagingRecord> StagePluginAsync(
        Stream zipStream,
        string originalFileName,
        CancellationToken ct = default)
    {
        // Ensure staging directory exists
        Directory.CreateDirectory(StagingRoot);

        // Save stream to temp file and compute hash
        var stagingId = Guid.NewGuid().ToString("N")[..12];
        var stagingDir = Path.Combine(StagingRoot, stagingId);
        Directory.CreateDirectory(stagingDir);

        var tempZipPath = Path.Combine(stagingDir, "_upload.zip");
        string fileHash;
        long fileSize;

        try
        {
            await using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write))
            {
                await zipStream.CopyToAsync(fs, ct);
            }

            fileSize = new FileInfo(tempZipPath).Length;
            fileHash = await ComputeFileHashAsync(tempZipPath, ct);

            // Check for duplicate uploads
            var existing = await FindStagedByHashAsync(fileHash, ct);
            if (existing != null)
            {
                // Clean up and return existing
                try { Directory.Delete(stagingDir, recursive: true); } catch { /* best effort */ }
                return existing;
            }

            // Extract zip
            var extractDir = Path.Combine(stagingDir, "plugin");
            ExtractZipToPluginDirectory(tempZipPath, extractDir);

            // Clean up zip file after extraction
            File.Delete(tempZipPath);

            // Find plugin.json (might be in root or single subdirectory)
            var pluginDir = ResolvePluginRootDirectory(extractDir);

            // Run validation
            var validationResults = _validationPipeline.Validate(pluginDir);
            var hasErrors = validationResults.Any(r => !r.Passed);
            var status = hasErrors ? StagingValidationStatus.Invalid : StagingValidationStatus.Valid;

            // Read manifest for metadata
            var manifest = TryReadManifest(pluginDir);

            // Persist staging record
            var record = await InsertStagingRecordAsync(new PluginStagingRecord
            {
                Id = 0, // assigned by DB
                Name = manifest?.Name ?? Path.GetFileNameWithoutExtension(originalFileName),
                Version = manifest?.Version ?? "unknown",
                Description = manifest?.Description,
                Author = manifest?.Author,
                FileHash = fileHash,
                FileSize = fileSize,
                FileName = originalFileName,
                StagingPath = pluginDir,
                ValidationStatus = status,
                UploadedAt = DateTimeOffset.UtcNow,
                ValidationResults = validationResults
            }, ct);

            _logger.LogInformation(
                "Plugin staged: {Name} v{Version} (id={StagingId}, status={Status}, {CheckCount} checks, {ErrorCount} errors)",
                record.Name, record.Version, record.Id, status,
                validationResults.Count, validationResults.Count(r => !r.Passed));

            return record;
        }
        catch
        {
            // Clean up on failure
            try { Directory.Delete(stagingDir, recursive: true); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>
    /// List all staged plugins.
    /// </summary>
    public async Task<IReadOnlyList<PluginStagingRecord>> GetStagedPluginsAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, name, version, description, author, file_hash, file_size,
                   file_name, staging_path, validation_status, uploaded_at,
                   approved_at, validation_results
            FROM core.plugin_staging
            ORDER BY uploaded_at DESC
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var results = new List<PluginStagingRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadStagingRecord(reader));
        }
        return results;
    }

    /// <summary>
    /// Get a specific staged plugin by ID.
    /// </summary>
    public async Task<PluginStagingRecord?> GetStagedPluginAsync(int id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, name, version, description, author, file_hash, file_size,
                   file_name, staging_path, validation_status, uploaded_at,
                   approved_at, validation_results
            FROM core.plugin_staging
            WHERE id = @id
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        return await reader.ReadAsync(ct) ? ReadStagingRecord(reader) : null;
    }

    /// <summary>
    /// Re-run validation on a staged plugin.
    /// </summary>
    public async Task<PluginStagingRecord> RevalidateAsync(int id, CancellationToken ct = default)
    {
        var staging = await GetStagedPluginAsync(id, ct)
            ?? throw new InvalidOperationException($"Staged plugin with ID {id} not found");

        if (staging.ValidationStatus == StagingValidationStatus.Approved)
            throw new InvalidOperationException("Cannot revalidate an already approved plugin");

        if (!Directory.Exists(staging.StagingPath))
            throw new InvalidOperationException($"Staging directory no longer exists: {staging.StagingPath}");

        var validationResults = _validationPipeline.Validate(staging.StagingPath);
        var hasErrors = validationResults.Any(r => !r.Passed);
        var status = hasErrors ? StagingValidationStatus.Invalid : StagingValidationStatus.Valid;

        return await UpdateStagingStatusAsync(id, status, validationResults, ct);
    }

    /// <summary>
    /// Approve a staged plugin: move to live plugins directory, load, and remove staging record.
    /// </summary>
    public async Task<PluginDescriptor?> ApproveAsync(int id, CancellationToken ct = default)
    {
        var staging = await GetStagedPluginAsync(id, ct)
            ?? throw new InvalidOperationException($"Staged plugin with ID {id} not found");

        if (staging.ValidationStatus == StagingValidationStatus.Approved)
            throw new InvalidOperationException("Plugin is already approved");

        if (staging.ValidationStatus == StagingValidationStatus.Pending)
            throw new InvalidOperationException(
                "Cannot approve plugin before validation has been run. Trigger validation first.");

        if (staging.ValidationStatus == StagingValidationStatus.Rejected)
            throw new InvalidOperationException("Cannot approve a rejected plugin. Upload again if needed.");

        if (staging.ValidationStatus == StagingValidationStatus.Invalid)
        {
            // Check if there are blocking errors
            if (staging.ValidationResults.Any(r => !r.Passed))
                throw new InvalidOperationException(
                    "Cannot approve plugin with validation errors. Re-validate or fix issues first.");
        }

        if (!Directory.Exists(staging.StagingPath))
            throw new InvalidOperationException($"Staging directory no longer exists: {staging.StagingPath}");

        // Move to live plugins directory
        var targetDir = Path.Combine("plugins", staging.Name);
        if (Directory.Exists(targetDir))
        {
            // Check if the plugin is already loaded — refuse to overwrite a running plugin
            if (_loader != null)
            {
                var loadedPlugins = _loader.LoadedPlugins;
                if (loadedPlugins.ContainsKey(staging.Name))
                {
                    throw new InvalidOperationException(
                        $"Plugin '{staging.Name}' is already loaded from '{targetDir}'. " +
                        "Uninstall or disable the existing plugin before approving a replacement.");
                }
            }

            _logger.LogWarning("Plugin directory '{TargetDir}' already exists. Removing before approve.", targetDir);
            Directory.Delete(targetDir, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
        Directory.Move(staging.StagingPath, targetDir);

        // Clean up empty staging parent if exists
        CleanupEmptyStagingDir(staging.StagingPath);

        // Update staging record
        await UpdateStagingStatusAsync(id, StagingValidationStatus.Approved, staging.ValidationResults, ct);

        // Load the plugin via PluginDirectoryLoader
        PluginDescriptor? descriptor = null;
        if (_loader != null)
        {
            try
            {
                descriptor = _loader.LoadPluginFromDirectory(targetDir);
                _logger.LogInformation(
                    "Approved plugin '{Name}' loaded successfully with {FeatureCount} features",
                    staging.Name, descriptor?.Features.Count ?? 0);
            }
            catch (Exception ex)
            {
                // Load failed — move files BACK to staging to restore consistent state.
                // Without this, staging_path in DB points to old location but files are in targetDir,
                // breaking subsequent retry (approve) and cleanup (reject).
                _logger.LogError(ex,
                    "Failed to load approved plugin '{Name}' from {Dir}. Moving files back to staging for retry.",
                    staging.Name, targetDir);
                try
                {
                    if (Directory.Exists(targetDir) && !Directory.Exists(staging.StagingPath))
                    {
                        var stagingParent = Path.GetDirectoryName(staging.StagingPath);
                        if (stagingParent != null)
                            Directory.CreateDirectory(stagingParent);
                        Directory.Move(targetDir, staging.StagingPath);
                    }
                }
                catch (Exception moveBackEx)
                {
                    _logger.LogError(moveBackEx,
                        "Failed to move files back to staging for plugin '{Name}'. Files may be orphaned at '{Dir}'.",
                        staging.Name, targetDir);
                }

                try
                {
                    await UpdateStagingStatusAsync(id, StagingValidationStatus.Valid, staging.ValidationResults, ct);
                }
                catch (Exception revertEx)
                {
                    _logger.LogError(revertEx, "Failed to revert staging status for plugin '{Name}'", staging.Name);
                }
                throw new InvalidOperationException(
                    $"Plugin failed to load: {ex.Message}. " +
                    "Status reverted — you can retry approval or reject.", ex);
            }
        }
        else
        {
            _logger.LogWarning(
                "Plugin '{Name}' approved and moved to '{TargetDir}' but PluginDirectoryLoader is not available. " +
                "Plugin will be loaded on next application restart.",
                staging.Name, targetDir);
        }

        // Remove staging record after successful approval.
        // If delete fails, the plugin is still loaded — don't propagate the error.
        try
        {
            await DeleteStagingRecordAsync(id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete staging record for approved plugin '{Name}' (id={Id}). Record may be orphaned.",
                staging.Name, id);
        }

        return descriptor;
    }

    /// <summary>
    /// Reject a staged plugin: clean up files and remove staging record.
    /// </summary>
    public async Task RejectAsync(int id, CancellationToken ct = default)
    {
        var staging = await GetStagedPluginAsync(id, ct)
            ?? throw new InvalidOperationException($"Staged plugin with ID {id} not found");

        if (staging.ValidationStatus == StagingValidationStatus.Approved)
            throw new InvalidOperationException("Cannot reject an already approved plugin");

        // Clean up staging files
        if (Directory.Exists(staging.StagingPath))
        {
            try
            {
                Directory.Delete(staging.StagingPath, recursive: true);
                CleanupEmptyStagingDir(staging.StagingPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up staging directory: {Path}", staging.StagingPath);
            }
        }

        // Update status then delete record
        await UpdateStagingStatusAsync(id, StagingValidationStatus.Rejected, staging.ValidationResults, ct);
        await DeleteStagingRecordAsync(id, ct);

        _logger.LogInformation("Staged plugin '{Name}' (id={Id}) rejected and cleaned up", staging.Name, id);
    }

    #region Database Operations

    private async Task<PluginStagingRecord> InsertStagingRecordAsync(
        PluginStagingRecord record, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO core.plugin_staging
                (name, version, description, author, file_hash, file_size,
                 file_name, staging_path, validation_status, validation_results)
            VALUES
                (@name, @version, @description, @author, @file_hash, @file_size,
                 @file_name, @staging_path, @validation_status, @validation_results::jsonb)
            RETURNING id, name, version, description, author, file_hash, file_size,
                      file_name, staging_path, validation_status, uploaded_at,
                      approved_at, validation_results
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@name", record.Name);
        cmd.Parameters.AddWithValue("@version", record.Version);
        cmd.Parameters.AddWithValue("@description", (object?)record.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@author", (object?)record.Author ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@file_hash", record.FileHash);
        cmd.Parameters.AddWithValue("@file_size", record.FileSize);
        cmd.Parameters.AddWithValue("@file_name", record.FileName);
        cmd.Parameters.AddWithValue("@staging_path", record.StagingPath);
        cmd.Parameters.AddWithValue("@validation_status", record.ValidationStatus.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@validation_results",
            JsonSerializer.Serialize(record.ValidationResults, JsonOptions));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("Failed to insert staging record");

        return ReadStagingRecord(reader);
    }

    private async Task<PluginStagingRecord> UpdateStagingStatusAsync(
        int id, StagingValidationStatus status,
        IReadOnlyList<ValidationCheckResult> results, CancellationToken ct)
    {
        var sql = status == StagingValidationStatus.Approved
            ? """
              UPDATE core.plugin_staging
              SET validation_status = @status, approved_at = now(), validation_results = @results::jsonb
              WHERE id = @id
              RETURNING id, name, version, description, author, file_hash, file_size,
                        file_name, staging_path, validation_status, uploaded_at,
                        approved_at, validation_results
              """
            : """
              UPDATE core.plugin_staging
              SET validation_status = @status, approved_at = NULL, validation_results = @results::jsonb
              WHERE id = @id
              RETURNING id, name, version, description, author, file_hash, file_size,
                        file_name, staging_path, validation_status, uploaded_at,
                        approved_at, validation_results
              """;

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@status", status.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@results",
            JsonSerializer.Serialize(results, JsonOptions));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException($"Staged plugin with ID {id} not found");

        return ReadStagingRecord(reader);
    }

    private async Task DeleteStagingRecordAsync(int id, CancellationToken ct)
    {
        const string sql = "DELETE FROM core.plugin_staging WHERE id = @id";

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<PluginStagingRecord?> FindStagedByHashAsync(string fileHash, CancellationToken ct)
    {
        const string sql = """
            SELECT id, name, version, description, author, file_hash, file_size,
                   file_name, staging_path, validation_status, uploaded_at,
                   approved_at, validation_results
            FROM core.plugin_staging
            WHERE file_hash = @hash AND validation_status NOT IN ('approved', 'rejected')
            LIMIT 1
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@hash", fileHash);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        return await reader.ReadAsync(ct) ? ReadStagingRecord(reader) : null;
    }

    private static PluginStagingRecord ReadStagingRecord(NpgsqlDataReader reader)
    {
        var validationResultsJson = reader.IsDBNull(reader.GetOrdinal("validation_results"))
            ? "[]"
            : reader.GetString(reader.GetOrdinal("validation_results"));

        var validationResults = JsonSerializer.Deserialize<List<ValidationCheckResult>>(
            validationResultsJson, JsonOptions) ?? [];

        var statusStr = reader.GetString(reader.GetOrdinal("validation_status"));
        var validationStatus = statusStr switch
        {
            "pending" => StagingValidationStatus.Pending,
            "valid" => StagingValidationStatus.Valid,
            "invalid" => StagingValidationStatus.Invalid,
            "approved" => StagingValidationStatus.Approved,
            "rejected" => StagingValidationStatus.Rejected,
            _ => StagingValidationStatus.Pending
        };

        return new PluginStagingRecord
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Version = reader.GetString(reader.GetOrdinal("version")),
            Description = reader.IsDBNull(reader.GetOrdinal("description"))
                ? null : reader.GetString(reader.GetOrdinal("description")),
            Author = reader.IsDBNull(reader.GetOrdinal("author"))
                ? null : reader.GetString(reader.GetOrdinal("author")),
            FileHash = reader.GetString(reader.GetOrdinal("file_hash")),
            FileSize = reader.GetInt64(reader.GetOrdinal("file_size")),
            FileName = reader.GetString(reader.GetOrdinal("file_name")),
            StagingPath = reader.GetString(reader.GetOrdinal("staging_path")),
            ValidationStatus = validationStatus,
            UploadedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("uploaded_at")),
            ApprovedAt = reader.IsDBNull(reader.GetOrdinal("approved_at"))
                ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("approved_at")),
            ValidationResults = validationResults
        };
    }

    #endregion

    #region File Helpers

    private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>Max total decompressed size to prevent zip bombs (500 MB).</summary>
    private const long MaxDecompressedSize = 500L * 1024 * 1024;

    private static void ExtractZipToPluginDirectory(string zipPath, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        // Validate total decompressed size before extraction to prevent zip bombs
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            long totalSize = 0;
            foreach (var entry in archive.Entries)
            {
                totalSize += entry.Length;
                if (totalSize > MaxDecompressedSize)
                {
                    throw new InvalidOperationException(
                        $"Plugin zip decompressed size exceeds {MaxDecompressedSize / (1024 * 1024)} MB limit. " +
                        "This may indicate a zip bomb.");
                }
            }
        }

        ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);
    }

    /// <summary>
    /// Resolve the actual plugin root directory (handles zip structures where
    /// plugin.json might be in the root or inside a single top-level subdirectory).
    /// </summary>
    private static string ResolvePluginRootDirectory(string extractDir)
    {
        if (File.Exists(Path.Combine(extractDir, "plugin.json")))
            return extractDir;

        // Check single top-level subdirectory
        var subdirs = Directory.GetDirectories(extractDir);
        if (subdirs.Length == 1 && File.Exists(Path.Combine(subdirs[0], "plugin.json")))
            return subdirs[0];

        // Fallback: return extract dir even without plugin.json (will fail validation)
        return extractDir;
    }

    private static PluginManifestFile? TryReadManifest(string pluginDir)
    {
        var manifestPath = Path.Combine(pluginDir, "plugin.json");
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<PluginManifestFile>(json, ManifestJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void CleanupEmptyStagingDir(string stagingPath)
    {
        try
        {
            var parent = Path.GetDirectoryName(stagingPath);
            if (parent != null && Directory.Exists(parent) &&
                !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                Directory.Delete(parent);
            }
        }
        catch { /* best effort */ }
    }

    #endregion
}
