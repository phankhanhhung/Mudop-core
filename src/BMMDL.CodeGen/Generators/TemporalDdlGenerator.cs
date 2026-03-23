using System.Text;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.CodeGen.Generators;

/// <summary>
/// Generates temporal DDL: history tables, versioning triggers, temporal columns and constraints.
/// </summary>
internal class TemporalDdlGenerator
{
    private readonly DdlGeneratorContext _ctx;

    public TemporalDdlGenerator(DdlGeneratorContext context)
    {
        _ctx = context;
    }

    /// <summary>
    /// Append temporal system columns to the column list.
    /// </summary>
    public void AppendTemporalColumns(List<string> columns, BmEntity entity)
    {
        // Only generate temporal DDL if the Temporal feature contributed metadata.
        // If the Temporal plugin is excluded, entity.Features won't have "Temporal"
        // even though entity.IsTemporal may be true from the @Temporal annotation.
        if (!entity.IsTemporal || !entity.Features.ContainsKey("Temporal")) return;
        string Q(string id) => NamingConvention.QuoteIdentifier(id);
        columns.Add($"    {Q("system_start")} TIMESTAMPTZ NOT NULL DEFAULT now()");
        columns.Add($"    {Q("system_end")} TIMESTAMPTZ NOT NULL DEFAULT 'infinity'::TIMESTAMPTZ");
        columns.Add($"    {Q("version")} INTEGER NOT NULL DEFAULT 1");
    }

    /// <summary>
    /// Append temporal PK/EXCLUDE constraints.
    /// Returns true if temporal constraints were added (caller should skip standard PK).
    /// </summary>
    public bool AppendTemporalConstraints(List<string> constraints, BmEntity entity)
    {
        if (!entity.IsTemporal || !entity.Features.ContainsKey("Temporal")) return false;
        string Q(string id) => NamingConvention.QuoteIdentifier(id);

        var keyFields = entity.Fields.Where(f => f.IsKey)
            .Select(f => Q(NamingConvention.GetColumnName(f.Name))).ToList();
        var primaryKeyColumns = string.Join(", ", keyFields);

        if (entity.TemporalStrategy == TemporalStrategy.InlineHistory)
        {
            if (entity.HasValidTime && !string.IsNullOrEmpty(entity.ValidTimeFromColumn))
            {
                var validFromCol = Q(NamingConvention.GetColumnName(entity.ValidTimeFromColumn));
                constraints.Add($"    PRIMARY KEY ({primaryKeyColumns}, {validFromCol}, {Q("system_start")})");
            }
            else
            {
                constraints.Add($"    PRIMARY KEY ({primaryKeyColumns}, {Q("system_start")})");
            }

            var excludeColumns = string.Join(", ", keyFields.Select(k => $"{k} WITH ="));
            var unqualifiedTable = _ctx.GetUnqualifiedTableName(entity);
            constraints.Add($"    CONSTRAINT {Q(unqualifiedTable + "_no_overlap")} EXCLUDE USING gist ({excludeColumns}, tstzrange({Q("system_start")}, {Q("system_end")}, '[)') WITH &&)");

            // Valid-time EXCLUDE constraint: prevent overlapping valid-time ranges for the same entity key
            if (entity.HasValidTime && !string.IsNullOrEmpty(entity.ValidTimeFromColumn) && !string.IsNullOrEmpty(entity.ValidTimeToColumn))
            {
                var validFromCol = Q(NamingConvention.GetColumnName(entity.ValidTimeFromColumn));
                var validToCol = Q(NamingConvention.GetColumnName(entity.ValidTimeToColumn));
                constraints.Add($"    CONSTRAINT {Q(unqualifiedTable + "_valid_time_no_overlap")} EXCLUDE USING gist ({excludeColumns}, tstzrange({validFromCol}::TIMESTAMPTZ, {validToCol}::TIMESTAMPTZ, '[)') WITH &&)");
            }
        }
        else // SeparateTables
        {
            constraints.Add($"    PRIMARY KEY ({primaryKeyColumns})");
        }

        return true;
    }

    /// <summary>
    /// Append temporal indexes, history tables, and versioning triggers after the CREATE TABLE.
    /// </summary>
    public void AppendTemporalPostTable(StringBuilder sb, BmEntity entity)
    {
        if (!entity.IsTemporal || !entity.Features.ContainsKey("Temporal")) return;
        string Q(string id) => NamingConvention.QuoteIdentifier(id);

        var tableName = QuoteQualifiedName(_ctx.GetQualifiedTableNameForEntity(entity));
        var unqualifiedTable = _ctx.GetUnqualifiedTableName(entity);
        var keyFields = entity.Fields.Where(f => f.IsKey)
            .Select(f => Q(NamingConvention.GetColumnName(f.Name))).ToList();

        if (entity.TemporalStrategy == TemporalStrategy.InlineHistory)
        {
            sb.AppendLine();
            sb.AppendLine($"-- Temporal: Partial index for current records lookup");
            var keyColumnsForIndex = string.Join(", ", keyFields);
            sb.AppendLine($"CREATE INDEX {Q($"idx_{unqualifiedTable}_current")} ON {tableName}({keyColumnsForIndex}) WHERE {Q("system_end")} = 'infinity'::TIMESTAMPTZ;");
        }
        else // SeparateTables
        {
            sb.AppendLine();
            sb.Append(GenerateTemporalHistoryTable(entity));
            sb.AppendLine();
            sb.Append(GenerateTemporalVersioningTrigger(entity));
        }
    }

    /// <summary>
    /// Generate history table for temporal entity with Separate Tables strategy.
    /// Creates {table}_history with compound PK (id, system_start).
    /// </summary>
    public string GenerateTemporalHistoryTable(BmEntity entity)
    {
        string Q(string id) => NamingConvention.QuoteIdentifier(id);

        var tableName = _ctx.GetQualifiedTableNameForEntity(entity);
        var unqualifiedTable = _ctx.GetUnqualifiedTableName(entity);
        var historyTableName = BuildHistoryTableName(tableName);

        var sb = new StringBuilder();
        sb.AppendLine($"-- History table for {entity.Namespace}.{entity.Name}");
        sb.AppendLine($"CREATE TABLE {historyTableName} (");

        var columns = new List<string>();
        var constraints = new List<string>();

        // Copy all columns from main entity
        foreach (var field in entity.Fields)
        {
            if (field.IsComputed && field.ComputedStrategy != BMMDL.MetaModel.Enums.ComputedStrategy.Application &&
                field.ComputedStrategy != BMMDL.MetaModel.Enums.ComputedStrategy.Trigger)
                continue;

            var resolved = _ctx.Resolver.Resolve(field);
            if (resolved.FlattenedFields != null)
            {
                foreach (var flatField in resolved.FlattenedFields)
                {
                    columns.Add($"    {flatField.ToColumnDefinition()}");
                }
            }
            else
            {
                var columnName = Q(NamingConvention.GetColumnName(field.Name));
                var nullClause = field.IsKey ? " NOT NULL" : "";
                columns.Add($"    {columnName} {resolved.PostgresType}{nullClause}");
            }
        }

        // Add aspect fields
        var historyFieldNames = new HashSet<string>(
            entity.Fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var aspectName in entity.Aspects)
        {
            if (_ctx.Cache.Aspects.TryGetValue(aspectName, out var aspect))
            {
                foreach (var field in aspect.Fields)
                {
                    if (!historyFieldNames.Contains(field.Name))
                    {
                        var resolved = _ctx.Resolver.Resolve(field);
                        var columnName = Q(NamingConvention.GetColumnName(field.Name));
                        columns.Add($"    {columnName} {resolved.PostgresType}");
                        historyFieldNames.Add(field.Name);
                    }
                }
            }
        }

        // Add FK columns (skip ManyToMany)
        foreach (var assoc in entity.Associations)
        {
            if (assoc.Cardinality == BmCardinality.ManyToMany)
                continue;
            var fkColumn = _ctx.GenerateForeignKeyColumn(assoc);
            columns.Add($"    {fkColumn}");
        }

        // System time columns and version
        columns.Add($"    {Q("system_start")} TIMESTAMPTZ NOT NULL");
        columns.Add($"    {Q("system_end")} TIMESTAMPTZ NOT NULL");
        columns.Add($"    {Q("version")} INTEGER NOT NULL");

        // Compound primary key
        var keyFields = entity.Fields.Where(f => f.IsKey)
            .Select(f => Q(NamingConvention.GetColumnName(f.Name))).ToList();
        var primaryKeyColumns = string.Join(", ", keyFields);
        constraints.Add($"    PRIMARY KEY ({primaryKeyColumns}, {Q("system_start")})");

        // EXCLUDE constraint
        var excludeColumns = string.Join(", ", keyFields.Select(k => $"{k} WITH ="));
        constraints.Add($"    CONSTRAINT {Q(unqualifiedTable + "_history_no_overlap")} EXCLUDE USING gist ({excludeColumns}, tstzrange({Q("system_start")}, {Q("system_end")}, '[)') WITH &&)");

        var allDefinitions = columns.Concat(constraints);
        sb.AppendLine(string.Join(",\n", allDefinitions));
        sb.AppendLine(");");

        // Index for fast history lookups
        sb.AppendLine();
        sb.AppendLine($"-- Index for history lookups by business key");
        var keyColumnsForIndex = string.Join(", ", keyFields);
        sb.AppendLine($"CREATE INDEX {Q($"idx_{unqualifiedTable}_history_key")} ON {historyTableName}({keyColumnsForIndex}, {Q("system_start")} DESC);");

        return sb.ToString();
    }

    /// <summary>
    /// Generate versioning trigger for temporal entity with Separate Tables strategy.
    /// Copies old record to history table before UPDATE/DELETE.
    /// </summary>
    public string GenerateTemporalVersioningTrigger(BmEntity entity)
    {
        string Q(string id) => NamingConvention.QuoteIdentifier(id);

        var tableName = QuoteQualifiedName(_ctx.GetQualifiedTableNameForEntity(entity));
        var unqualifiedTable = _ctx.GetUnqualifiedTableName(entity);
        var historyTableName = BuildHistoryTableName(_ctx.GetQualifiedTableNameForEntity(entity));
        var functionName = $"{unqualifiedTable}_versioning";
        var triggerName = $"tr_{unqualifiedTable}_versioning";

        var columnNames = new List<string>();
        foreach (var field in entity.Fields)
        {
            if (field.IsComputed && field.ComputedStrategy != BMMDL.MetaModel.Enums.ComputedStrategy.Application &&
                field.ComputedStrategy != BMMDL.MetaModel.Enums.ComputedStrategy.Trigger)
                continue;

            var resolved = _ctx.Resolver.Resolve(field);
            if (resolved.FlattenedFields != null)
            {
                foreach (var flatField in resolved.FlattenedFields)
                {
                    columnNames.Add(flatField.ColumnName);
                }
            }
            else
            {
                columnNames.Add(NamingConvention.GetColumnName(field.Name));
            }
        }

        // Add aspect fields
        var knownFieldNames = new HashSet<string>(
            entity.Fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var aspectName in entity.Aspects)
        {
            if (_ctx.Cache.Aspects.TryGetValue(aspectName, out var aspect))
            {
                foreach (var field in aspect.Fields)
                {
                    if (!knownFieldNames.Contains(field.Name))
                    {
                        columnNames.Add(NamingConvention.GetColumnName(field.Name));
                        knownFieldNames.Add(field.Name);
                    }
                }
            }
        }

        // Add FK columns (skip ManyToMany)
        foreach (var assoc in entity.Associations)
        {
            if (assoc.Cardinality == BmCardinality.ManyToMany)
                continue;
            columnNames.Add(NamingConvention.GetFkColumnName(assoc.Name));
        }

        // System time columns and version
        columnNames.Add("system_start");
        columnNames.Add("system_end");
        columnNames.Add("version");

        var columnsStr = string.Join(", ", columnNames.Select(Q));
        var oldColumnValues = columnNames.Select(c =>
            c == "system_end" ? "now()" : $"OLD.{Q(c)}").ToList();
        var oldColumnsStr = string.Join(", ", oldColumnValues);

        var sb = new StringBuilder();
        sb.AppendLine($"-- Versioning trigger for {entity.Namespace}.{entity.Name}");
        sb.AppendLine($"CREATE OR REPLACE FUNCTION {Q(functionName)}()");
        sb.AppendLine("RETURNS TRIGGER AS $$");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    IF TG_OP = 'UPDATE' OR TG_OP = 'DELETE' THEN");
        sb.AppendLine($"        -- Copy old record to history with system_end = now()");
        sb.AppendLine($"        INSERT INTO {historyTableName} ({columnsStr})");
        sb.AppendLine($"        VALUES ({oldColumnsStr});");
        sb.AppendLine("    END IF;");
        sb.AppendLine();
        sb.AppendLine("    IF TG_OP = 'DELETE' THEN");
        sb.AppendLine("        RETURN OLD;");
        sb.AppendLine("    ELSE");
        sb.AppendLine("        -- Update system_start and increment version for new version");
        sb.AppendLine($"        NEW.{Q("system_start")} := now();");
        sb.AppendLine($"        NEW.{Q("system_end")} := 'infinity'::TIMESTAMPTZ;");
        sb.AppendLine($"        NEW.{Q("version")} := OLD.{Q("version")} + 1;");
        sb.AppendLine("        RETURN NEW;");
        sb.AppendLine("    END IF;");
        sb.AppendLine("END;");
        sb.AppendLine("$$ LANGUAGE plpgsql;");
        sb.AppendLine();
        sb.AppendLine($"DROP TRIGGER IF EXISTS {Q(triggerName)} ON {tableName};");
        sb.AppendLine($"CREATE TRIGGER {Q(triggerName)}");
        sb.AppendLine($"    BEFORE UPDATE OR DELETE ON {tableName}");
        sb.AppendLine($"    FOR EACH ROW EXECUTE FUNCTION {Q(functionName)}();");

        return sb.ToString();
    }

    /// <summary>
    /// Quote a potentially qualified name (schema.table -> "schema"."table", or table -> "table").
    /// </summary>
    private static string QuoteQualifiedName(string qualifiedName)
    {
        var dotIndex = qualifiedName.IndexOf('.');
        if (dotIndex >= 0)
        {
            var schema = qualifiedName[..dotIndex];
            var name = qualifiedName[(dotIndex + 1)..];
            return $"{NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(name)}";
        }
        return NamingConvention.QuoteIdentifier(qualifiedName);
    }

    /// <summary>
    /// Build a quoted history table name from an unquoted qualified table name.
    /// E.g., "hr.employees" -> "hr"."employees_history"
    /// </summary>
    private static string BuildHistoryTableName(string qualifiedTableName)
    {
        var dotIndex = qualifiedTableName.IndexOf('.');
        if (dotIndex >= 0)
        {
            var schema = qualifiedTableName[..dotIndex];
            var table = qualifiedTableName[(dotIndex + 1)..];
            return $"{NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(table + "_history")}";
        }
        return NamingConvention.QuoteIdentifier(qualifiedTableName + "_history");
    }
}
