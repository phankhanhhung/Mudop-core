using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Types;
using BMMDL.Compiler.Utilities;

namespace BMMDL.Compiler.Validation;

/// <summary>
/// Validates computed field definitions for correctness
/// </summary>
public class ComputedFieldValidator
{
    private readonly List<ValidationError> _errors = new();
    
    /// <summary>
    /// Validate all computed fields in an entity
    /// </summary>
    public List<ValidationError> Validate(BmEntity entity)
    {
        _errors.Clear();
        
        foreach (var field in entity.Fields.Where(f => f.IsComputed))
        {
            ValidateField(entity, field);
        }
        
        // Check for circular dependencies across all computed fields
        DetectCircularDependencies(entity);
        
        return _errors.ToList();
    }
    
    private void ValidateField(BmEntity entity, BmField field)
    {
        if (field.ComputedExpr == null)
        {
            AddError(field, "Computed field must have an expression",
                $"Field '{field.Name}' is marked as computed but has no ComputedExpr");
            return;
        }
        
        // Validate field references
        ValidateFieldReferences(entity, field, field.ComputedExpr);
        
        // Type compatibility checking
        ValidateTypeCompatibility(entity, field);
    }
    
    private void ValidateTypeCompatibility(BmEntity entity, BmField field)
    {
        if (field.ComputedExpr == null || field.TypeRef == null)
            return;
            
        var exprType = InferExpressionType(entity, field.ComputedExpr);
        if (exprType == null)
            return; // Can't infer, skip validation
            
        var fieldType = GetTypeName(field.TypeRef);
        
        // Check if types are compatible
        if (!AreTypesCompatible(fieldType, exprType))
        {
            AddError(field, "Type mismatch in computed field",
                $"Computed field '{field.Name}' has type '{fieldType}' but expression evaluates to '{exprType}'");
        }
    }
    
    private string? GetTypeName(BmTypeReference typeRef)
    {
        return typeRef switch
        {
            BmPrimitiveType primitive => primitive.Kind.ToString().ToLowerInvariant(),
            BmCustomTypeReference custom => custom.TypeName?.ToLowerInvariant(),
            BmEntityTypeReference entity => entity.EntityName?.ToLowerInvariant(),
            _ => null
        };
    }
    
    private string? InferExpressionType(BmEntity entity, BmExpression expr)
    {
        return expr switch
        {
            BmLiteralExpression lit => InferLiteralType(lit),
            BmIdentifierExpression ident => InferIdentifierType(entity, ident),
            BmBinaryExpression bin => InferBinaryType(entity, bin),
            BmFunctionCallExpression func => InferFunctionType(func),
            BmCastExpression cast => GetTypeName(cast.TargetType),
            _ => null
        };
    }
    
    private string InferLiteralType(BmLiteralExpression lit)
    {
        return lit.Kind switch
        {
            BmLiteralKind.Integer => "integer",
            BmLiteralKind.Decimal => "decimal",
            BmLiteralKind.String => "string",
            BmLiteralKind.Boolean => "boolean",
            _ => "unknown"
        };
    }
    
    private string? InferIdentifierType(BmEntity entity, BmIdentifierExpression ident)
    {
        var fieldName = ident.Path.FirstOrDefault();
        if (string.IsNullOrEmpty(fieldName))
            return null;
            
        var field = entity.Fields.FirstOrDefault(f => 
            f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
            
        return field?.TypeRef != null ? GetTypeName(field.TypeRef) : null;
    }
    
    private string? InferBinaryType(BmEntity entity, BmBinaryExpression bin)
    {
        var leftType = InferExpressionType(entity, bin.Left);
        var rightType = InferExpressionType(entity, bin.Right);
        
        if (leftType == null || rightType == null)
            return null;
            
        // String concatenation
        if (bin.Operator == BmBinaryOperator.Concat || 
            (bin.Operator == BmBinaryOperator.Add && (leftType == "string" || rightType == "string")))
        {
            return "string";
        }
        
        // Numeric operations
        if (IsNumericOperator(bin.Operator))
        {
            // Promote to decimal if either is decimal
            if (leftType == "decimal" || rightType == "decimal")
                return "decimal";
            return "integer";
        }
        
        // Comparison operators return boolean
        if (IsComparisonOperator(bin.Operator))
        {
            return "boolean";
        }
        
        // Logical operators return boolean
        if (bin.Operator == BmBinaryOperator.And || bin.Operator == BmBinaryOperator.Or)
        {
            return "boolean";
        }
        
        return leftType; // Default to left type
    }
    
    private string InferFunctionType(BmFunctionCallExpression func)
    {
        // Common function return types
        return func.FunctionName.ToUpperInvariant() switch
        {
            "UPPER" or "LOWER" or "TRIM" or "SUBSTRING" or "CONCAT" => "string",
            "LENGTH" or "ROUND" or "FLOOR" or "CEIL" or "ABS" => "integer",
            _ => "unknown"
        };
    }
    
    private bool IsNumericOperator(BmBinaryOperator op)
    {
        return op == BmBinaryOperator.Add || 
               op == BmBinaryOperator.Subtract || 
               op == BmBinaryOperator.Multiply || 
               op == BmBinaryOperator.Divide ||
               op == BmBinaryOperator.Modulo;
    }
    
    private bool IsComparisonOperator(BmBinaryOperator op)
    {
        return op == BmBinaryOperator.Equal || 
               op == BmBinaryOperator.NotEqual ||
               op == BmBinaryOperator.LessThan || 
               op == BmBinaryOperator.GreaterThan ||
               op == BmBinaryOperator.LessOrEqual || 
               op == BmBinaryOperator.GreaterOrEqual;
    }
    
    private bool AreTypesCompatible(string? fieldType, string? exprType)
    {
        if (fieldType == null || exprType == null || exprType == "unknown")
            return true; // Can't validate, assume OK
            
        // Exact match
        if (fieldType == exprType)
            return true;
            
        // Numeric promotions: integer can be assigned to decimal
        if (fieldType == "decimal" && exprType == "integer")
            return true;
            
        // Amount/Quantity are decimal/integer aliases
        if ((fieldType == "amount" || fieldType == "price") && (exprType == "decimal" || exprType == "integer"))
            return true;
        if (fieldType == "quantity" && exprType == "integer")
            return true;
            
        return false;
    }
    
    private void ValidateFieldReferences(BmEntity entity, BmField computedField, BmExpression expr)
    {
        BmExpressionWalker.Walk(expr, node =>
        {
            if (node is BmIdentifierExpression ident)
                ValidateIdentifier(entity, computedField, ident);
        });
    }
    
    private void ValidateIdentifier(BmEntity entity, BmField computedField, BmIdentifierExpression ident)
    {
        var fieldName = ident.Path.FirstOrDefault();
        if (string.IsNullOrEmpty(fieldName))
            return;
        
        // Check if field exists in entity
        var referencedField = entity.Fields.FirstOrDefault(f => 
            f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        
        if (referencedField == null)
        {
            AddError(computedField, $"Unknown field reference '{fieldName}'",
                $"Field '{fieldName}' referenced in computed field '{computedField.Name}' does not exist in entity '{entity.Name}'");
        }
    }
    
    private void DetectCircularDependencies(BmEntity entity)
    {
        var computedFields = entity.Fields.Where(f => f.IsComputed).ToList();
        var checkedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var field in computedFields)
        {
            if (checkedFields.Contains(field.Name))
                continue; // Already checked this field
                
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var path = new List<string>();
            
            if (HasCircularDependency(entity, field.Name, visiting, path))
            {
                var cycle = string.Join(" → ", path);
                var errorField = entity.Fields.First(f => f.Name == field.Name);
                AddError(errorField, "Circular dependency detected",
                    $"Computed field '{field.Name}' has circular dependency: {cycle}");
            }
            
            checkedFields.Add(field.Name);
        }
    }
    
    private bool HasCircularDependency(BmEntity entity, string fieldName, 
        HashSet<string> visiting, List<string> path)
    {
        // If we're currently visiting this field, we found a cycle
        if (visiting.Contains(fieldName))
        {
            path.Add(fieldName);
            return true;
        }
        
        var field = entity.Fields.FirstOrDefault(f => 
            f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        
        // Not a computed field - no cycle through this path
        if (field == null || !field.IsComputed || field.ComputedExpr == null)
            return false;
        
        // Mark as visiting
        visiting.Add(fieldName);
        path.Add(fieldName);
        
        // Get all field dependencies from expression
        var dependencies = GetDependencies(field.ComputedExpr);
        
        foreach (var depName in dependencies)
        {
            if (HasCircularDependency(entity, depName, visiting, path))
            {
                return true;
            }
        }
        
        // Unmark visiting (backtrack)
        visiting.Remove(fieldName);
        path.RemoveAt(path.Count - 1);
        return false;
    }
    
    private HashSet<string> GetDependencies(BmExpression? expr)
    {
        return ExpressionTraversalUtility.CollectIdentifiers(expr);
    }
    
    private void AddError(BmField field, string title, string message)
    {
        _errors.Add(new ValidationError
        {
            FieldName = field.Name,
            Title = title,
            Message = message,
            SourceFile = field.SourceFile,
            StartLine = field.StartLine,
            EndLine = field.EndLine
        });
    }
}

/// <summary>
/// Represents a validation error
/// </summary>
public class ValidationError
{
    public string FieldName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    
    public override string ToString()
    {
        var location = SourceFile != null 
            ? $"{SourceFile}:{StartLine}" 
            : $"line {StartLine}";
        return $"Error at {location}: {Title}\n  {Message}";
    }
}
