using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;
using BMMDL.CodeGen.Visitors;

namespace BMMDL.CodeGen;

/// <summary>
/// Translates BMMDL expressions to PostgreSQL SQL expressions.
/// Uses AST visitor pattern - respects that AST is the source of truth.
/// </summary>
public class ExpressionTranslator
{
    private readonly BmEntity _entity;
    private readonly PostgresSqlExpressionVisitor _visitor;
    
    public ExpressionTranslator(BmEntity entity)
    {
        _entity = entity;
        _visitor = new PostgresSqlExpressionVisitor(entity);
    }
    
    /// <summary>
    /// Translate BMMDL expression AST to PostgreSQL SQL.
    /// Works directly from AST nodes - no string manipulation shortcuts.
    /// </summary>
    public string Translate(BmExpression expr)
    {
        return _visitor.Visit(expr);
    }
}

