using Npgsql;
using Microsoft.Extensions.Logging;

namespace BMMDL.Registry.Services;

/// <summary>
/// Executes migration SQL scripts against PostgreSQL database.
/// </summary>
public class MigrationExecutor
{
    private readonly ILogger<MigrationExecutor>? _logger;

    public MigrationExecutor(ILogger<MigrationExecutor>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Execute a migration script (UP or DOWN).
    /// </summary>
    public async Task<MigrationExecutionResult> ExecuteAsync(
        string connectionString,
        string sql,
        string? migrationName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new MigrationExecutionResult
            {
                Success = true,
                Message = "No SQL to execute"
            };
        }

        var startTime = DateTime.UtcNow;
        
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            // Execute in a transaction
            await using var transaction = await conn.BeginTransactionAsync(ct);
            
            try
            {
                // Split by semicolons but handle $$ blocks
                var statements = SplitSqlStatements(sql);
                var statementsExecuted = 0;

                foreach (var statement in statements)
                {
                    if (string.IsNullOrWhiteSpace(statement)) continue;

                    // Skip standalone BEGIN/COMMIT statements — the executor already wraps
                    // in its own NpgsqlTransaction, so embedded transaction control would
                    // commit prematurely and leave subsequent statements outside the transaction.
                    var trimmedStmt = statement.TrimEnd(';').Trim();
                    if (trimmedStmt.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) ||
                        trimmedStmt.Equals("COMMIT", StringComparison.OrdinalIgnoreCase))
                        continue;

                    await using var cmd = new NpgsqlCommand(statement, conn, transaction);
                    cmd.CommandTimeout = 300; // 5 minutes for large migrations
                    await cmd.ExecuteNonQueryAsync(ct);
                    statementsExecuted++;
                }

                await transaction.CommitAsync(ct);

                var duration = DateTime.UtcNow - startTime;
                _logger?.LogInformation(
                    "Migration {Name} executed successfully: {Count} statements in {Duration:F2}s",
                    migrationName ?? "unnamed", statementsExecuted, duration.TotalSeconds);

                return new MigrationExecutionResult
                {
                    Success = true,
                    StatementsExecuted = statementsExecuted,
                    Duration = duration,
                    Message = $"Executed {statementsExecuted} statements"
                };
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Migration {Name} failed: {Message}", migrationName ?? "unnamed", ex.Message);
            
            return new MigrationExecutionResult
            {
                Success = false,
                Error = ex.Message,
                Duration = DateTime.UtcNow - startTime
            };
        }
    }

    /// <summary>
    /// Execute a migration plan (all UP scripts).
    /// </summary>
    public async Task<MigrationExecutionResult> ExecutePlanAsync(
        string connectionString,
        MigrationPlan plan,
        CancellationToken ct = default)
    {
        var totalStatements = 0;
        var startTime = DateTime.UtcNow;
        var completedSteps = new List<string>();

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            if (string.IsNullOrWhiteSpace(step.UpSql)) continue;

            _logger?.LogInformation(
                "Executing migration step {Step}/{Total}: {Description}",
                i + 1, plan.Steps.Count, step.Description);

            var result = await ExecuteAsync(connectionString, step.UpSql, step.Description, ct);
            
            if (result.Success)
            {
                totalStatements += result.StatementsExecuted;
                completedSteps.Add(step.Description ?? $"step_{i}");
            }
            else
            {
                // Stop on first failure — do not continue with subsequent steps
                _logger?.LogError(
                    "Migration step {Step}/{Total} failed: {Description} — {Error}. " +
                    "Aborting remaining {Remaining} steps. Completed steps: [{Completed}]",
                    i + 1, plan.Steps.Count, step.Description, result.Error,
                    plan.Steps.Count - i - 1,
                    string.Join(", ", completedSteps));

                return new MigrationExecutionResult
                {
                    Success = false,
                    StatementsExecuted = totalStatements,
                    Duration = DateTime.UtcNow - startTime,
                    Message = $"Failed at step {i + 1}/{plan.Steps.Count} ({step.Description}): {result.Error}",
                    Error = $"Step '{step.Description}' failed: {result.Error}. " +
                            $"Completed {completedSteps.Count} of {plan.Steps.Count} steps before failure."
                };
            }
        }

        return new MigrationExecutionResult
        {
            Success = true,
            StatementsExecuted = totalStatements,
            Duration = DateTime.UtcNow - startTime,
            Message = $"Executed {totalStatements} statements across {plan.Steps.Count} steps"
        };
    }

    /// <summary>
    /// Execute rollback using DOWN scripts in reverse order.
    /// </summary>
    public async Task<MigrationExecutionResult> ExecuteRollbackPlanAsync(
        string connectionString,
        MigrationPlan plan,
        CancellationToken ct = default)
    {
        var totalStatements = 0;
        var startTime = DateTime.UtcNow;
        var errors = new List<string>();

        // Execute DOWN scripts in REVERSE order
        var reversedSteps = plan.Steps.AsEnumerable().Reverse().ToList();

        foreach (var step in reversedSteps)
        {
            if (string.IsNullOrWhiteSpace(step.DownSql)) continue;

            var result = await ExecuteAsync(connectionString, step.DownSql, $"Rollback: {step.Description}", ct);
            
            if (result.Success)
            {
                totalStatements += result.StatementsExecuted;
            }
            else
            {
                errors.Add($"Rollback {step.Description}: {result.Error}");
                // Stop on first error during rollback - don't continue with partial rollback
                break;
            }
        }

        return new MigrationExecutionResult
        {
            Success = errors.Count == 0,
            StatementsExecuted = totalStatements,
            Duration = DateTime.UtcNow - startTime,
            Message = errors.Count == 0 
                ? $"Rolled back {totalStatements} statements across {reversedSteps.Count} steps"
                : string.Join("; ", errors),
            Error = errors.Count > 0 ? string.Join("; ", errors) : null
        };
    }

    /// <summary>
    /// Split SQL into statements, handling $$ function blocks.
    /// </summary>
    private static List<string> SplitSqlStatements(string sql)
    {
        var statements = new List<string>();
        var current = new System.Text.StringBuilder();
        var inDollarBlock = false;
        var lines = sql.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            // Check for $$ blocks (function definitions)
            var dollarCount = CountOccurrences(trimmed, "$$");
            if (dollarCount % 2 == 1)
                inDollarBlock = !inDollarBlock;

            current.AppendLine(line);

            // If we're not in a $$ block and line ends with ;
            if (!inDollarBlock && trimmed.EndsWith(";"))
            {
                var stmt = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(stmt))
                    statements.Add(stmt);
                current.Clear();
            }
        }

        // Add any remaining content
        var remaining = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(remaining))
            statements.Add(remaining);

        return statements;
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}

public class MigrationExecutionResult
{
    public bool Success { get; set; }
    public int StatementsExecuted { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}
