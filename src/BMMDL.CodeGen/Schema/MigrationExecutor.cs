using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using BMMDL.CodeGen.Schema;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.CodeGen.Schema;

/// <summary>
/// Executes database migrations with transaction support and history tracking.
/// </summary>
public class MigrationExecutor
{
    private readonly string _connectionString;
    private readonly string _schema;

    private string QuotedSchema => NamingConvention.QuoteIdentifier(_schema);
    private string QuotedMigrationTable => $"{NamingConvention.QuoteIdentifier(_schema)}.{NamingConvention.QuoteIdentifier("__migrations")}";

    public MigrationExecutor(string connectionString, string schema = "platform")
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _schema = schema;
    }
    
    /// <summary>
    /// Ensures migration history table exists.
    /// </summary>
    public async Task EnsureMigrationTableAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var sql = $@"
            CREATE SCHEMA IF NOT EXISTS {QuotedSchema};
            CREATE TABLE IF NOT EXISTS {QuotedMigrationTable} (
                id SERIAL PRIMARY KEY,
                name VARCHAR(255) NOT NULL UNIQUE,
                applied_at TIMESTAMP NOT NULL DEFAULT NOW(),
                up_script TEXT NOT NULL,
                down_script TEXT NOT NULL,
                checksum VARCHAR(64) NOT NULL
            );";
        
        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }
    
    /// <summary>
    /// Applies a migration with transaction support.
    /// </summary>
    public async Task<MigrationResult> ApplyMigrationAsync(Migration migration, bool dryRun = false)
    {
        if (dryRun)
        {
            return new MigrationResult
            {
                Success = true,
                Script = migration.UpScript,
                DryRun = true
            };
        }
        
        await EnsureMigrationTableAsync();
        
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // Atomically claim the migration slot using INSERT ... ON CONFLICT DO NOTHING.
            // This eliminates the TOCTOU race between a separate SELECT check and INSERT.
            // If the script fails, the transaction rolls back and the record is removed.
            var inserted = await RecordMigrationAtomicAsync(connection, transaction, migration);
            if (!inserted)
            {
                await transaction.RollbackAsync();
                return new MigrationResult
                {
                    Success = false,
                    Error = $"Migration '{migration.Name}' already applied"
                };
            }

            // Execute migration script (after claiming the slot)
            if (!string.IsNullOrWhiteSpace(migration.UpScript))
            {
                await using var cmd = new NpgsqlCommand(migration.UpScript, connection, transaction);
                await cmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            
            return new MigrationResult
            {
                Success = true,
                AppliedAt = DateTime.UtcNow,
                Script = migration.UpScript
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            
            // For PostgreSQL errors, extract position and show relevant SQL snippet
            var errorDetail = ex.Message;
            if (ex is NpgsqlException npgEx)
            {
                // Try to extract position from PostgreSQL error
                var positionMatch = System.Text.RegularExpressions.Regex.Match(ex.Message, @"POSITION:\s*(\d+)");
                if (positionMatch.Success && int.TryParse(positionMatch.Groups[1].Value, out var position))
                {
                    var script = migration.UpScript ?? "";
                    var start = Math.Max(0, position - 200);
                    var length = Math.Min(500, script.Length - start);
                    var snippet = start < script.Length ? script.Substring(start, length) : "(out of range)";
                    errorDetail = $"{ex.Message}\nSQL near position {position}: ...{snippet}...";
                }
            }
            
            return new MigrationResult
            {
                Success = false,
                Error = errorDetail
            };
        }
    }
    
    /// <summary>
    /// Rolls back the last migration.
    /// </summary>
    public async Task<MigrationResult> RollbackMigrationAsync(string migrationName)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var migration = await GetMigrationAsync(connection, migrationName);
        if (migration == null)
        {
            return new MigrationResult
            {
                Success = false,
                Error = $"Migration '{migrationName}' not found in history"
            };
        }
        
        await using var transaction = await connection.BeginTransactionAsync();
        
        try
        {
            // Execute down script
            if (!string.IsNullOrWhiteSpace(migration.DownScript))
            {
                await using var cmd = new NpgsqlCommand(migration.DownScript, connection, transaction);
                await cmd.ExecuteNonQueryAsync();
            }
            
            // Remove from history
            await RemoveMigrationAsync(connection, transaction, migrationName);
            
            await transaction.CommitAsync();
            
            return new MigrationResult
            {
                Success = true,
                Script = migration.DownScript
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            
            return new MigrationResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
    
    /// <summary>
    /// Gets all applied migrations.
    /// </summary>
    public async Task<List<Migration>> GetAppliedMigrationsAsync()
    {
        await EnsureMigrationTableAsync();
        
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var migrations = new List<Migration>();
        
        var sql = $@"
            SELECT name, applied_at, up_script, down_script, checksum
            FROM {QuotedMigrationTable}
            ORDER BY applied_at DESC";
        
        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            migrations.Add(new Migration
            {
                Name = reader.GetString(0),
                Timestamp = reader.GetDateTime(1),
                UpScript = reader.GetString(2),
                DownScript = reader.GetString(3),
                Checksum = reader.GetString(4)
            });
        }
        
        return migrations;
    }
    
    private async Task<bool> IsMigrationAppliedAsync(NpgsqlConnection connection, string name)
    {
        try
        {
            var sql = $"SELECT COUNT(*) FROM {QuotedMigrationTable} WHERE name = @name";
            await using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("name", name);
            
            var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            return count > 0;
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Table doesn't exist yet, migration definitely not applied
            return false;
        }
    }
    
    /// <summary>
    /// Atomically inserts a migration record using ON CONFLICT DO NOTHING.
    /// Returns true if the row was inserted, false if it already existed.
    /// </summary>
    private async Task<bool> RecordMigrationAtomicAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Migration migration)
    {
        var sql = $@"
            INSERT INTO {QuotedMigrationTable} (name, up_script, down_script, checksum)
            VALUES (@name, @upScript, @downScript, @checksum)
            ON CONFLICT (name) DO NOTHING";

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("name", migration.Name);
        cmd.Parameters.AddWithValue("upScript", migration.UpScript);
        cmd.Parameters.AddWithValue("downScript", migration.DownScript);
        cmd.Parameters.AddWithValue("checksum", migration.Checksum);

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }
    
    private async Task<Migration?> GetMigrationAsync(NpgsqlConnection connection, string name)
    {
        try
        {
            var sql = $@"
                SELECT name, applied_at, up_script, down_script, checksum
                FROM {QuotedMigrationTable}
                WHERE name = @name";
            
            await using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("name", name);
            
            await using var reader = await cmd.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                return new Migration
                {
                    Name = reader.GetString(0),
                    Timestamp = reader.GetDateTime(1),
                    UpScript = reader.GetString(2),
                    DownScript = reader.GetString(3),
                    Checksum = reader.GetString(4)
                };
            }
            
            return null;
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Table doesn't exist, migration not found
            return null;
        }
    }
    
    private async Task RemoveMigrationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string name)
    {
        var sql = $"DELETE FROM {QuotedMigrationTable} WHERE name = @name";
        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("name", name);
        await cmd.ExecuteNonQueryAsync();
    }
}
