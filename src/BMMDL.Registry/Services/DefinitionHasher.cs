using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;

namespace BMMDL.Registry.Services;

/// <summary>
/// Computes deterministic hashes for meta model object definitions.
/// Used for change detection between versions.
/// </summary>
public class DefinitionHasher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Compute hash for an entity definition.
    /// </summary>
    public string HashEntity(BmEntity entity)
    {
        var parts = new List<string>
        {
            $"name:{entity.Name}",
            $"namespace:{entity.Namespace}",
            $"extends:{entity.ExtendsFrom ?? "null"}",
            $"temporal:{entity.IsTemporal}",
            $"tenantScoped:{entity.TenantScoped}"
        };

        // Sort fields for determinism
        var fieldHashes = entity.Fields
            .OrderBy(f => f.Name)
            .Select(f => $"{f.Name}:{HashField(f)}")
            .ToList();
        parts.Add($"fields:[{string.Join(",", fieldHashes)}]");

        // Sort associations
        var assocHashes = entity.Associations
            .OrderBy(a => a.Name)
            .Select(a => $"{a.Name}:{a.TargetEntity}:{a.Cardinality}")
            .ToList();
        parts.Add($"assocs:[{string.Join(",", assocHashes)}]");

        // Sort indexes
        var indexHashes = entity.Indexes
            .OrderBy(i => i.Name)
            .Select(HashIndex)
            .ToList();
        parts.Add($"indexes:[{string.Join(",", indexHashes)}]");

        // Aspects
        var aspects = entity.Aspects
            .OrderBy(a => a)
            .ToList();
        parts.Add($"aspects:[{string.Join(",", aspects)}]");

        return ComputeSha256(string.Join("|", parts));
    }

    /// <summary>
    /// Compute hash for a field definition.
    /// </summary>
    public string HashField(BmField field)
    {
        var parts = new List<string>
        {
            $"name:{field.Name}",
            $"type:{field.TypeRef?.ToTypeString() ?? field.TypeString}",
            $"key:{field.IsKey}",
            $"nullable:{field.IsNullable}",
            $"computed:{field.IsComputed}",
            $"virtual:{field.IsVirtual}",
            $"readonly:{field.IsReadonly}",
            $"immutable:{field.IsImmutable}"
        };

        // Include expression hashes if present
        if (field.ComputedExpr != null)
        {
            parts.Add($"computedHash:{HashExpression(field.ComputedExpr)}");
        }

        if (field.DefaultExpr != null)
        {
            parts.Add($"defaultHash:{HashExpression(field.DefaultExpr)}");
        }

        return ComputeSha256(string.Join("|", parts));
    }

    /// <summary>
    /// Compute hash for an enum definition.
    /// </summary>
    public string HashEnum(BmEnum enumDef)
    {
        var parts = new List<string>
        {
            $"name:{enumDef.Name}"
        };

        // Values in order (order matters for enums)
        var values = enumDef.Values.Select(v => v.Name).ToList();
        parts.Add($"values:[{string.Join(",", values)}]");

        return ComputeSha256(string.Join("|", parts));
    }

    /// <summary>
    /// Compute hash for a type definition.
    /// </summary>
    public string HashType(BmType typeDef)
    {
        var parts = new List<string>
        {
            $"name:{typeDef.Name}",
            $"base:{typeDef.BaseType}"
        };

        // Sort fields for determinism
        var fieldHashes = typeDef.Fields
            .OrderBy(f => f.Name)
            .Select(f => $"{f.Name}:{HashField(f)}")
            .ToList();
        parts.Add($"fields:[{string.Join(",", fieldHashes)}]");

        return ComputeSha256(string.Join("|", parts));
    }

    /// <summary>
    /// Compute hash for an expression AST.
    /// </summary>
    public string HashExpression(BmExpression expr)
    {
        var structureString = GetExpressionStructure(expr);
        return ComputeSha256(structureString);
    }

    /// <summary>
    /// Compute hash for an index definition.
    /// </summary>
    public string HashIndex(BmIndex index)
    {
        var parts = new List<string>
        {
            $"name:{index.Name}",
            $"unique:{index.IsUnique}"
        };

        // Fields in order (order matters for indexes)
        parts.Add($"fields:[{string.Join(",", index.Fields)}]");

        return ComputeSha256(string.Join("|", parts));
    }

    /// <summary>
    /// Compute hash for an access rule.
    /// </summary>
    public string HashAccessRule(BmAccessRule rule)
    {
        var parts = new List<string>
        {
            $"type:{rule.RuleType}",
            $"operations:[{string.Join(",", rule.Operations.OrderBy(o => o))}]",
            $"scope:{rule.Scope?.ToString() ?? "null"}"
        };

        if (rule.Principal != null)
        {
            parts.Add($"principal:{rule.Principal.Type}:[{string.Join(",", rule.Principal.Values.OrderBy(v => v))}]");
        }

        if (rule.WhereConditionExpr != null)
        {
            parts.Add($"where:{HashExpression(rule.WhereConditionExpr)}");
        }

        return ComputeSha256(string.Join("|", parts));
    }

    /// <summary>
    /// Get a string representation of expression structure (for hashing).
    /// </summary>
    private string GetExpressionStructure(BmExpression? expr)
    {
        if (expr == null) return "NULL";

        return expr switch
        {
            BmLiteralExpression lit => $"LIT:{lit.Kind}:{lit.Value}",
            BmIdentifierExpression id => $"ID:{string.Join(".", id.Path)}",
            BmContextVariableExpression ctx => $"CTX:{ctx.FullPath}",
            BmParameterExpression param => $"PARAM:{param.Name}",
            BmBinaryExpression bin => $"BIN:{GetExpressionStructure(bin.Left)}|{bin.Operator}|{GetExpressionStructure(bin.Right)}",
            BmUnaryExpression un => $"UN:{un.Operator}|{GetExpressionStructure(un.Operand)}",
            BmFunctionCallExpression fn => $"FN:{fn.FunctionName}({string.Join(",", fn.Arguments.Select(GetExpressionStructure))})",
            BmAggregateExpression agg => $"AGG:{agg.Function}:{agg.IsDistinct}:{GetExpressionStructure(agg.Argument)}:{GetExpressionStructure(agg.WhereCondition)}",
            BmCaseExpression cs => BuildCaseStructure(cs),
            BmCastExpression cast => $"CAST:{GetExpressionStructure(cast.Expression)}:{cast.TargetType.ToTypeString()}",
            BmTernaryExpression tern => $"TERN:{GetExpressionStructure(tern.Condition)}?{GetExpressionStructure(tern.ThenExpression)}:{GetExpressionStructure(tern.ElseExpression)}",
            BmInExpression @in => $"IN:{GetExpressionStructure(@in.Expression)}:[{string.Join(",", @in.Values.Select(GetExpressionStructure))}]:{@in.IsNot}",
            BmBetweenExpression btw => $"BTW:{GetExpressionStructure(btw.Expression)}:{GetExpressionStructure(btw.Low)}:{GetExpressionStructure(btw.High)}:{btw.IsNot}",
            BmLikeExpression like => $"LIKE:{GetExpressionStructure(like.Expression)}:{GetExpressionStructure(like.Pattern)}:{like.IsNot}",
            BmIsNullExpression isn => $"ISNULL:{GetExpressionStructure(isn.Expression)}:{isn.IsNot}",
            BmParenExpression paren => $"PAREN:{GetExpressionStructure(paren.Inner)}",
            _ => $"UNKNOWN:{expr.GetType().Name}"
        };
    }

    private string BuildCaseStructure(BmCaseExpression cs)
    {
        var parts = new List<string> { "CASE" };
        
        if (cs.InputExpression != null)
        {
            parts.Add($"INPUT:{GetExpressionStructure(cs.InputExpression)}");
        }

        foreach (var (when, then) in cs.WhenClauses)
        {
            parts.Add($"WHEN:{GetExpressionStructure(when)}:THEN:{GetExpressionStructure(then)}");
        }

        if (cs.ElseResult != null)
        {
            parts.Add($"ELSE:{GetExpressionStructure(cs.ElseResult)}");
        }

        return string.Join("|", parts);
    }

    /// <summary>
    /// Compute SHA256 hash of input string.
    /// </summary>
    private string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Serialize a definition to JSON for snapshot storage.
    /// </summary>
    public string SerializeToJson<T>(T definition)
    {
        return JsonSerializer.Serialize(definition, JsonOptions);
    }
}
