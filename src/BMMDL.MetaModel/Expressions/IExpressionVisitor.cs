namespace BMMDL.MetaModel.Expressions;

/// <summary>
/// Visitor pattern interface for traversing and transforming BmExpression AST trees.
/// This is the proper way to work with expressions - AST is the source of truth.
/// </summary>
/// <typeparam name="T">The result type of visiting expressions</typeparam>
public interface IExpressionVisitor<T>
{
    /// <summary>
    /// Visit any expression (dispatcher method)
    /// </summary>
    T Visit(BmExpression expression);
    
    /// <summary>
    /// Visit a literal expression: 'text', 123, 45.67, true, null, #EnumValue
    /// </summary>
    T VisitLiteral(BmLiteralExpression literal);
    
    /// <summary>
    /// Visit an identifier expression: fieldName, entity.field
    /// </summary>
    T VisitIdentifier(BmIdentifierExpression identifier);
    
    /// <summary>
    /// Visit a context variable: $now, $user, $tenant
    /// </summary>
    T VisitContextVariable(BmContextVariableExpression contextVar);
    
    /// <summary>
    /// Visit a parameter reference: :customerId, :startDate
    /// </summary>
    T VisitParameter(BmParameterExpression parameter);
    
    /// <summary>
    /// Visit a binary expression: left op right
    /// </summary>
    T VisitBinary(BmBinaryExpression binary);
    
    /// <summary>
    /// Visit a unary expression: op operand
    /// </summary>
    T VisitUnary(BmUnaryExpression unary);
    
    /// <summary>
    /// Visit a function call: UPPER(name), SUBSTRING(text, 1, 10)
    /// </summary>
    T VisitFunctionCall(BmFunctionCallExpression functionCall);
    
    /// <summary>
    /// Visit a type cast: price::INTEGER, CAST(amount AS DECIMAL)
    /// </summary>
    T VisitCast(BmCastExpression cast);
    
    /// <summary>
    /// Visit a CASE expression: CASE WHEN ... THEN ... ELSE ... END
    /// </summary>
    T VisitCase(BmCaseExpression caseExpr);
}

