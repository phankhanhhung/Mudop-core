using System.Text;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.CodeGen.Generators;

/// <summary>
/// Generates trigger DDL: sequence triggers and computed field triggers.
/// </summary>
internal class TriggerDdlGenerator
{
    private readonly DdlGeneratorContext _ctx;

    public TriggerDdlGenerator(DdlGeneratorContext context)
    {
        _ctx = context;
    }

    /// <summary>
    /// Generate BEFORE INSERT trigger for sequence fields.
    /// </summary>
    public string GenerateSequenceTrigger(BmEntity entity)
    {
        string Q(string id) => NamingConvention.QuoteIdentifier(id);
        static string SqlEscape(string? value) => value?.Replace("'", "''") ?? "";

        var unqualifiedTableName = NamingConvention.GetTableName(entity);
        var qualifiedTableName = QuoteQualifiedName(_ctx.GetQualifiedTableNameForEntity(entity));
        var sequenceFields = entity.Fields
            .Where(f => f.GetAnnotation("Sequence.Name")?.Value != null)
            .ToList();

        if (sequenceFields.Count == 0)
            return string.Empty;

        var functionName = $"{unqualifiedTableName}_seq_trigger";
        var triggerName = $"{unqualifiedTableName}_seq_trg";

        var sb = new StringBuilder();
        sb.AppendLine($"-- Sequence trigger for {unqualifiedTableName}");
        sb.AppendLine($"CREATE OR REPLACE FUNCTION {Q(functionName)}()");
        sb.AppendLine("RETURNS TRIGGER AS $$");
        sb.AppendLine("BEGIN");

        foreach (var field in sequenceFields)
        {
            var columnName = NamingConvention.GetColumnName(field.Name);
            var seqName = field.GetAnnotation("Sequence.Name")?.Value as string;
            var pattern = (field.GetAnnotation("Sequence.Pattern")?.Value as string) ?? "{seq}";
            var scope = (field.GetAnnotation("Sequence.Scope")?.Value as string) ?? "Company";
            var resetOn = (field.GetAnnotation("Sequence.ResetOn")?.Value as string) ?? "Never";
            if (resetOn.StartsWith("#")) resetOn = resetOn[1..];

            sb.AppendLine($"    IF NEW.{Q(columnName)} IS NULL THEN");
            sb.AppendLine($"        NEW.{Q(columnName)} := get_next_sequence_value(");
            sb.AppendLine($"            '{SqlEscape(seqName)}',");
            sb.AppendLine($"            current_setting('app.tenant_id', true)::UUID,");
            sb.AppendLine($"            current_setting('app.company_id', true)::UUID,");
            sb.AppendLine($"            '{SqlEscape(pattern)}', '{SqlEscape(scope)}', '{SqlEscape(resetOn)}'");
            sb.AppendLine($"        );");
            sb.AppendLine($"    END IF;");
        }

        sb.AppendLine("    RETURN NEW;");
        sb.AppendLine("END;");
        sb.AppendLine("$$ LANGUAGE plpgsql;");
        sb.AppendLine();
        sb.AppendLine($"DROP TRIGGER IF EXISTS {Q(triggerName)} ON {qualifiedTableName};");
        sb.AppendLine($"CREATE TRIGGER {Q(triggerName)}");
        sb.AppendLine($"    BEFORE INSERT ON {qualifiedTableName}");
        sb.AppendLine($"    FOR EACH ROW EXECUTE FUNCTION {Q(functionName)}();");

        return sb.ToString();
    }

    /// <summary>
    /// Generate trigger function and trigger for a computed field with Trigger strategy.
    /// </summary>
    public string GenerateComputedFieldTrigger(BmEntity entity, BmField computedField)
    {
        string Q(string id) => NamingConvention.QuoteIdentifier(id);

        if (!computedField.IsComputed || computedField.ComputedExpr == null)
            return string.Empty;

        var tableName = NamingConvention.GetTableName(entity);
        var qualifiedTableName = QuoteQualifiedName(_ctx.GetQualifiedTableNameForEntity(entity));
        var columnName = NamingConvention.GetColumnName(computedField.Name);
        var functionName = $"compute_{tableName}_{columnName}";
        var triggerName = $"tr_{tableName}_compute_{columnName}";

        var translator = new ExpressionTranslator(entity);
        var expression = translator.Translate(computedField.ComputedExpr);

        var dependentFields = FindDependentFields(computedField.ComputedExpr, entity);
        var updateOfClause = dependentFields.Count > 0
            ? $"OF {string.Join(", ", dependentFields.Select(f => Q(NamingConvention.GetColumnName(f))))} "
            : "";

        var sb = new StringBuilder();

        sb.AppendLine($"-- Trigger function for computed field {entity.Name}.{computedField.Name}");
        sb.AppendLine($"CREATE OR REPLACE FUNCTION {Q(functionName)}()");
        sb.AppendLine("RETURNS TRIGGER AS $$");
        sb.AppendLine("BEGIN");
        sb.AppendLine($"    NEW.{Q(columnName)} := {expression};");
        sb.AppendLine("    RETURN NEW;");
        sb.AppendLine("END;");
        sb.AppendLine("$$ LANGUAGE plpgsql;");
        sb.AppendLine();

        sb.AppendLine($"CREATE TRIGGER {Q(triggerName)}");
        sb.AppendLine($"    BEFORE INSERT OR UPDATE {updateOfClause}ON {qualifiedTableName}");
        sb.AppendLine("    FOR EACH ROW");
        sb.AppendLine($"    EXECUTE FUNCTION {Q(functionName)}();");

        return sb.ToString();
    }

    /// <summary>
    /// Generate all triggers for computed fields with Trigger strategy in an entity.
    /// </summary>
    public string GenerateAllComputedFieldTriggers(BmEntity entity)
    {
        var triggerFields = entity.Fields
            .Where(f => f.IsComputed &&
                        f.ComputedExpr != null &&
                        f.ComputedStrategy == BMMDL.MetaModel.Enums.ComputedStrategy.Trigger)
            .ToList();

        if (triggerFields.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"-- ============================================");
        sb.AppendLine($"-- Computed Field Triggers for {entity.Namespace}.{entity.Name}");
        sb.AppendLine($"-- ============================================");
        sb.AppendLine();

        foreach (var field in triggerFields)
        {
            sb.AppendLine(GenerateComputedFieldTrigger(entity, field));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Quote a potentially qualified name (schema.table → "schema"."table", or table → "table").
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
    /// Find field names referenced in an expression for UPDATE OF clause.
    /// </summary>
    private List<string> FindDependentFields(BmExpression expr, BmEntity entity)
    {
        var result = new HashSet<string>();
        FindDependentFieldsRecursive(expr, entity, result);
        return result.ToList();
    }

    private void FindDependentFieldsRecursive(BmExpression expr, BmEntity entity, HashSet<string> result)
    {
        switch (expr)
        {
            case BmIdentifierExpression id:
                if (id.Path.Count == 1)
                {
                    var fieldName = id.Path[0];
                    if (entity.Fields.Any(f => f.Name == fieldName))
                    {
                        result.Add(fieldName);
                    }
                }
                break;

            case BmBinaryExpression bin:
                FindDependentFieldsRecursive(bin.Left, entity, result);
                FindDependentFieldsRecursive(bin.Right, entity, result);
                break;

            case BmUnaryExpression un:
                FindDependentFieldsRecursive(un.Operand, entity, result);
                break;

            case BmFunctionCallExpression func:
                foreach (var arg in func.Arguments)
                    FindDependentFieldsRecursive(arg, entity, result);
                break;

            case BmCaseExpression caseExpr:
                foreach (var (when, then) in caseExpr.WhenClauses)
                {
                    FindDependentFieldsRecursive(when, entity, result);
                    FindDependentFieldsRecursive(then, entity, result);
                }
                if (caseExpr.ElseResult != null)
                    FindDependentFieldsRecursive(caseExpr.ElseResult, entity, result);
                break;
        }
    }
}
