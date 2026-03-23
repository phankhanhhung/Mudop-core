using System.Text;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.Registry.Services;

/// <summary>
/// Generates PostgreSQL PL/pgSQL functions from BMMDL action/function bodies.
/// </summary>
public class PgSqlActionGenerator
{
    private readonly string _schemaName;
    private readonly PgSqlExpressionTranslator _exprTranslator;

    public PgSqlActionGenerator(string schemaName = "public")
    {
        _schemaName = schemaName;
        _exprTranslator = new PgSqlExpressionTranslator();
    }

    /// <summary>
    /// Quote a PostgreSQL identifier (schema, table, function, column names).
    /// Escapes double quotes by doubling them.


    /// <summary>
    /// Escape a string literal for use in SQL (single quotes doubled).
    /// </summary>
    private static string EscapeString(string value)
    {
        return value.Replace("'", "''");
    }

    /// <summary>
    /// Generate PL/pgSQL function from a BMMDL action definition.
    /// </summary>
    public PgSqlFunctionResult GenerateActionFunction(
        BmEntity entity,
        BmAction action)
    {
        var tableName = NamingConvention.ToSnakeCase(entity.Name);
        var functionName = $"{tableName}_{NamingConvention.ToSnakeCase(action.Name)}";
        
        var result = new PgSqlFunctionResult
        {
            FunctionName = functionName,
            EntityName = entity.Name,
            ActionName = action.Name
        };

        var sb = new StringBuilder();

        // Function header (identifiers quoted for safety)
        var quotedSchema = NamingConvention.QuoteIdentifier(_schemaName);
        var quotedFunction = NamingConvention.QuoteIdentifier(functionName);
        var quotedTable = NamingConvention.QuoteIdentifier(tableName);
        sb.AppendLine($"CREATE OR REPLACE FUNCTION {quotedSchema}.{quotedFunction}(");

        // Parameters: p_id (entity id), plus action parameters
        var parameters = new List<string> { "p_id UUID" };
        parameters.Add("p_user_id UUID DEFAULT NULL");
        parameters.Add("p_tenant_id UUID DEFAULT NULL");
        
        foreach (var param in action.Parameters)
        {
            var sqlType = SqlTypeMapper.MapToSqlType(param.Type);
            parameters.Add($"p_{NamingConvention.ToSnakeCase(param.Name)} {sqlType}");
        }

        sb.AppendLine("    " + string.Join(",\n    ", parameters));
        sb.AppendLine(")");

        // Return type
        var returnType = MapReturnType(action.ReturnType);
        sb.AppendLine($"RETURNS {returnType} AS $$");
        
        // Declare section
        sb.AppendLine("DECLARE");
        sb.AppendLine($"    v_this RECORD;");
        sb.AppendLine($"    v_result {returnType};");
        
        // Add variable declarations from let statements
        foreach (var stmt in action.Body.OfType<BmLetStatement>())
        {
            sb.AppendLine($"    v_{NamingConvention.ToSnakeCase(stmt.VariableName)} TEXT;");
        }
        
        sb.AppendLine("BEGIN");

        // Load $this context (using quoted identifiers)
        sb.AppendLine($"    -- Load entity context");
        sb.AppendLine($"    SELECT * INTO v_this FROM {quotedSchema}.{quotedTable} WHERE id = p_id;");
        sb.AppendLine();
        
        // Generate body statements
        foreach (var stmt in action.Body)
        {
            GenerateStatement(sb, stmt, entity, "    ");
        }
        
        // Return
        if (returnType != "void")
        {
            sb.AppendLine("    RETURN v_result;");
        }
        else
        {
            sb.AppendLine("    RETURN;");
        }
        
        sb.AppendLine("END;");
        sb.AppendLine("$$ LANGUAGE plpgsql;");

        result.CreateFunctionSql = sb.ToString();
        result.DropFunctionSql = $"DROP FUNCTION IF EXISTS {quotedSchema}.{quotedFunction};";

        return result;
    }

    /// <summary>
    /// Generate PL/pgSQL function from a BMMDL function definition.
    /// </summary>
    public PgSqlFunctionResult GenerateFunction(
        BmEntity entity,
        BmFunction function)
    {
        var tableName = NamingConvention.ToSnakeCase(entity.Name);
        var functionName = $"{tableName}_{NamingConvention.ToSnakeCase(function.Name)}";
        
        var result = new PgSqlFunctionResult
        {
            FunctionName = functionName,
            EntityName = entity.Name,
            ActionName = function.Name
        };

        var sb = new StringBuilder();

        // Function header (identifiers quoted for safety)
        var quotedSchema = NamingConvention.QuoteIdentifier(_schemaName);
        var quotedFunction = NamingConvention.QuoteIdentifier(functionName);
        var quotedTable = NamingConvention.QuoteIdentifier(tableName);
        sb.AppendLine($"CREATE OR REPLACE FUNCTION {quotedSchema}.{quotedFunction}(");

        var parameters = new List<string> { "p_id UUID" };
        foreach (var param in function.Parameters)
        {
            var sqlType = SqlTypeMapper.MapToSqlType(param.Type);
            parameters.Add($"p_{NamingConvention.ToSnakeCase(param.Name)} {sqlType}");
        }

        sb.AppendLine("    " + string.Join(",\n    ", parameters));
        sb.AppendLine(")");

        var returnType = SqlTypeMapper.MapToSqlType(function.ReturnType);
        sb.AppendLine($"RETURNS {returnType} AS $$");
        
        sb.AppendLine("DECLARE");
        sb.AppendLine($"    v_this RECORD;");
        sb.AppendLine($"    v_result {returnType};");
        
        foreach (var stmt in function.Body.OfType<BmLetStatement>())
        {
            sb.AppendLine($"    v_{NamingConvention.ToSnakeCase(stmt.VariableName)} TEXT;");
        }
        
        sb.AppendLine("BEGIN");

        sb.AppendLine($"    SELECT * INTO v_this FROM {quotedSchema}.{quotedTable} WHERE id = p_id;");
        sb.AppendLine();

        foreach (var stmt in function.Body)
        {
            GenerateFunctionStatement(sb, stmt, entity, "    ");
        }

        sb.AppendLine("    RETURN v_result;");
        sb.AppendLine("END;");
        sb.AppendLine("$$ LANGUAGE plpgsql;");

        result.CreateFunctionSql = sb.ToString();
        result.DropFunctionSql = $"DROP FUNCTION IF EXISTS {quotedSchema}.{quotedFunction};";
        
        return result;
    }

    private void GenerateStatement(StringBuilder sb, BmRuleStatement stmt, BmEntity entity, string indent)
    {
        switch (stmt)
        {
            case BmValidateStatement validate:
                GenerateValidateStatement(sb, validate, indent);
                break;
                
            case BmComputeStatement compute:
                GenerateComputeStatement(sb, compute, entity, indent);
                break;
                
            case BmLetStatement let:
                GenerateLetStatement(sb, let, indent);
                break;
                
            case BmEmitStatement emit:
                GenerateEmitStatement(sb, emit, indent);
                break;
                
            case BmForeachStatement foreach_:
                GenerateForeachStatement(sb, foreach_, entity, indent);
                break;
                
            case BmReturnStatement return_:
                GenerateReturnStatement(sb, return_, indent);
                break;
                
            case BmRaiseStatement raise:
                GenerateRaiseStatement(sb, raise, indent);
                break;
                
            case BmWhenStatement whenStmt:
                GenerateWhenStatement(sb, whenStmt, entity, indent);
                break;
                
            case BmCallStatement call:
                GenerateCallStatement(sb, call, indent);
                break;
        }
    }

    private void GenerateFunctionStatement(StringBuilder sb, BmRuleStatement stmt, BmEntity entity, string indent)
    {
        switch (stmt)
        {
            case BmLetStatement let:
                GenerateLetStatement(sb, let, indent);
                break;
                
            case BmReturnStatement return_:
                GenerateReturnStatement(sb, return_, indent);
                break;
                
            case BmWhenStatement whenStmt:
                GenerateWhenStatement(sb, whenStmt, entity, indent, isFunction: true);
                break;
        }
    }

    private void GenerateValidateStatement(StringBuilder sb, BmValidateStatement stmt, string indent)
    {
        var condition = _exprTranslator.Translate(stmt.ExpressionAst) ?? stmt.Expression;
        var message = EscapeString(stmt.Message ?? "Validation failed");
        var severity = stmt.Severity == BmSeverity.Error ? "EXCEPTION" : "WARNING";

        sb.AppendLine($"{indent}-- Validate: {stmt.Expression}");
        sb.AppendLine($"{indent}IF NOT ({condition}) THEN");
        sb.AppendLine($"{indent}    RAISE {severity} '{message}';");
        sb.AppendLine($"{indent}END IF;");
        sb.AppendLine();
    }

    private void GenerateComputeStatement(StringBuilder sb, BmComputeStatement stmt, BmEntity entity, string indent)
    {
        var target = stmt.Target;
        var value = _exprTranslator.Translate(stmt.ExpressionAst) ?? stmt.Expression;
        var tableName = NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(entity.Name));
        var columnName = NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(target));
        var schemaName = NamingConvention.QuoteIdentifier(_schemaName);

        sb.AppendLine($"{indent}-- Compute: {target} = {stmt.Expression}");
        sb.AppendLine($"{indent}UPDATE {schemaName}.{tableName}");
        sb.AppendLine($"{indent}SET {columnName} = {value}");
        sb.AppendLine($"{indent}WHERE id = p_id;");
        sb.AppendLine();
    }

    private void GenerateLetStatement(StringBuilder sb, BmLetStatement stmt, string indent)
    {
        var varName = $"v_{NamingConvention.ToSnakeCase(stmt.VariableName)}";
        var value = _exprTranslator.Translate(stmt.ExpressionAst) ?? stmt.Expression;
        
        sb.AppendLine($"{indent}-- Let: {stmt.VariableName} = {stmt.Expression}");
        sb.AppendLine($"{indent}{varName} := {value};");
        sb.AppendLine();
    }

    private void GenerateEmitStatement(StringBuilder sb, BmEmitStatement stmt, string indent)
    {
        var eventName = EscapeString(NamingConvention.ToSnakeCase(stmt.EventName));
        var schemaName = NamingConvention.QuoteIdentifier(_schemaName);

        sb.AppendLine($"{indent}-- Emit event: {stmt.EventName}");
        sb.AppendLine($"{indent}INSERT INTO {schemaName}.\"domain_events\" (");
        sb.AppendLine($"{indent}    \"event_type\", \"entity_id\", \"user_id\", \"tenant_id\", \"payload\", \"created_at\"");
        sb.AppendLine($"{indent}) VALUES (");
        sb.AppendLine($"{indent}    '{eventName}',");
        sb.AppendLine($"{indent}    p_id,");
        sb.AppendLine($"{indent}    p_user_id,");
        sb.AppendLine($"{indent}    p_tenant_id,");

        // Build payload JSON from field assignments
        if (stmt.FieldAssignments.Count > 0)
        {
            var fields = stmt.FieldAssignments.Select(kv =>
            {
                var value = _exprTranslator.Translate(kv.Value) ?? "NULL";
                // JSON key should be escaped
                return $"'{EscapeString(kv.Key)}', {value}";
            });
            sb.AppendLine($"{indent}    jsonb_build_object({string.Join(", ", fields)}),");
        }
        else
        {
            sb.AppendLine($"{indent}    '{{}}'::jsonb,");
        }

        sb.AppendLine($"{indent}    NOW()");
        sb.AppendLine($"{indent});");
        sb.AppendLine();
    }

    private void GenerateForeachStatement(StringBuilder sb, BmForeachStatement stmt, BmEntity entity, string indent)
    {
        var varName = $"v_{NamingConvention.ToSnakeCase(stmt.VariableName)}";
        var collection = _exprTranslator.Translate(stmt.CollectionAst) ?? stmt.Collection;
        
        sb.AppendLine($"{indent}-- Foreach: {stmt.VariableName} in {stmt.Collection}");
        sb.AppendLine($"{indent}FOR {varName} IN {collection} LOOP");
        
        foreach (var bodyStmt in stmt.Body)
        {
            GenerateStatement(sb, bodyStmt, entity, indent + "    ");
        }
        
        sb.AppendLine($"{indent}END LOOP;");
        sb.AppendLine();
    }

    private void GenerateReturnStatement(StringBuilder sb, BmReturnStatement stmt, string indent)
    {
        var value = _exprTranslator.Translate(stmt.ExpressionAst) ?? stmt.Expression;
        
        sb.AppendLine($"{indent}v_result := {value};");
    }

    private void GenerateRaiseStatement(StringBuilder sb, BmRaiseStatement stmt, string indent)
    {
        var severity = stmt.Severity == BmSeverity.Error ? "EXCEPTION"
                     : stmt.Severity == BmSeverity.Warning ? "WARNING"
                     : "NOTICE";
        var message = EscapeString(stmt.Message ?? "");

        sb.AppendLine($"{indent}RAISE {severity} '{message}';");
        sb.AppendLine();
    }

    private void GenerateWhenStatement(StringBuilder sb, BmWhenStatement stmt, BmEntity entity, string indent, bool isFunction = false)
    {
        var condition = _exprTranslator.Translate(stmt.ConditionAst) ?? stmt.Condition;
        
        sb.AppendLine($"{indent}IF {condition} THEN");
        
        foreach (var thenStmt in stmt.ThenStatements)
        {
            if (isFunction)
                GenerateFunctionStatement(sb, thenStmt, entity, indent + "    ");
            else
                GenerateStatement(sb, thenStmt, entity, indent + "    ");
        }
        
        if (stmt.ElseStatements.Count > 0)
        {
            sb.AppendLine($"{indent}ELSE");
            foreach (var elseStmt in stmt.ElseStatements)
            {
                if (isFunction)
                    GenerateFunctionStatement(sb, elseStmt, entity, indent + "    ");
                else
                    GenerateStatement(sb, elseStmt, entity, indent + "    ");
            }
        }
        
        sb.AppendLine($"{indent}END IF;");
        sb.AppendLine();
    }

    private void GenerateCallStatement(StringBuilder sb, BmCallStatement stmt, string indent)
    {
        var functionName = NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(stmt.Target));
        var schemaName = NamingConvention.QuoteIdentifier(_schemaName);
        var args = string.Join(", ", stmt.Arguments.Select(a => _exprTranslator.Translate(a) ?? "NULL"));

        sb.AppendLine($"{indent}PERFORM {schemaName}.{functionName}({args});");
        sb.AppendLine();
    }

    private static string MapReturnType(string bmType) => bmType switch
    {
        "void" or "" => "void",
        _ => SqlTypeMapper.MapToSqlType(bmType)
    };
}

/// <summary>
/// Translates BMMDL expressions to PostgreSQL SQL expressions.
/// </summary>
public class PgSqlExpressionTranslator
{
    public string? Translate(BmExpression? expr)
    {
        if (expr == null) return null;
        
        return expr switch
        {
            BmLiteralExpression lit => TranslateLiteral(lit),
            BmIdentifierExpression id => TranslateIdentifier(id),
            BmBinaryExpression bin => TranslateBinary(bin),
            BmUnaryExpression unary => TranslateUnary(unary),
            BmContextVariableExpression ctx => TranslateContextVariable(ctx),
            BmFunctionCallExpression func => TranslateFunctionCall(func),
            BmParameterExpression param => TranslateParameter(param),
            BmParenExpression paren => $"({Translate(paren.Inner)})",
            _ => null
        };
    }

    private string TranslateLiteral(BmLiteralExpression lit) => lit.Kind switch
    {
        BmLiteralKind.Null => "NULL",
        BmLiteralKind.String => $"'{lit.Value?.ToString()?.Replace("'", "''") ?? ""}'",
        BmLiteralKind.Boolean => lit.Value is true ? "TRUE" : "FALSE",
        _ => lit.Value?.ToString() ?? "NULL"
    };

    private string TranslateIdentifier(BmIdentifierExpression id) =>
        $"v_this.{NamingConvention.ToSnakeCase(id.FullPath)}";

    private string TranslateBinary(BmBinaryExpression bin)
    {
        var left = Translate(bin.Left) ?? "NULL";
        var right = Translate(bin.Right) ?? "NULL";
        var op = bin.Operator switch
        {
            BmBinaryOperator.Equal => "=",
            BmBinaryOperator.NotEqual => "<>",
            BmBinaryOperator.LessThan => "<",
            BmBinaryOperator.GreaterThan => ">",
            BmBinaryOperator.LessOrEqual => "<=",
            BmBinaryOperator.GreaterOrEqual => ">=",
            BmBinaryOperator.And => "AND",
            BmBinaryOperator.Or => "OR",
            BmBinaryOperator.Add => "+",
            BmBinaryOperator.Subtract => "-",
            BmBinaryOperator.Multiply => "*",
            BmBinaryOperator.Divide => "/",
            BmBinaryOperator.Modulo => "%",
            BmBinaryOperator.Concat => "||",
            _ => "="
        };
        return $"({left} {op} {right})";
    }

    private string TranslateUnary(BmUnaryExpression unary)
    {
        var operand = Translate(unary.Operand) ?? "NULL";
        return unary.Operator switch
        {
            BmUnaryOperator.Not => $"NOT ({operand})",
            BmUnaryOperator.Negate => $"-({operand})",
            BmUnaryOperator.Plus => operand,
            _ => operand
        };
    }

    private string TranslateContextVariable(BmContextVariableExpression ctx)
    {
        var root = ctx.Root;
        return root switch
        {
            "this" => "v_this",
            "user" => "p_user_id",
            "tenant" => "p_tenant_id",
            "now" => "NOW()",
            "today" => "CURRENT_DATE",
            _ => ctx.FullPath
        };
    }

    private string TranslateParameter(BmParameterExpression param) =>
        $"p_{NamingConvention.ToSnakeCase(param.Name)}";

    private string TranslateFunctionCall(BmFunctionCallExpression func)
    {
        var args = func.Arguments.Select(Translate).ToList();
        var argsStr = string.Join(", ", args);
        
        return func.FunctionName.ToUpperInvariant() switch
        {
            "CONCAT" => $"CONCAT({argsStr})",
            "COALESCE" => $"COALESCE({argsStr})",
            "LENGTH" => $"LENGTH({argsStr})",
            "UPPER" => $"UPPER({argsStr})",
            "LOWER" => $"LOWER({argsStr})",
            "TRIM" => $"TRIM({argsStr})",
            "ROUND" => $"ROUND({argsStr})",
            "ABS" => $"ABS({argsStr})",
            "SUM" => $"SUM({argsStr})",
            "COUNT" => $"COUNT({argsStr})",
            "AVG" => $"AVG({argsStr})",
            "MIN" => $"MIN({argsStr})",
            "MAX" => $"MAX({argsStr})",
            _ => $"{func.FunctionName}({argsStr})"
        };
    }
}

#region Result Types

public class PgSqlFunctionResult
{
    public string FunctionName { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string ActionName { get; set; } = "";
    public string CreateFunctionSql { get; set; } = "";
    public string DropFunctionSql { get; set; } = "";
}

#endregion
