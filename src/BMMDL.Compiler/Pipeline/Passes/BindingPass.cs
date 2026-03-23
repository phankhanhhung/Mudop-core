using BMMDL.MetaModel;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Abstractions;
using BMMDL.Compiler.Parsing;
using BMMDL.Compiler.Utilities;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 4.7: Binding and Type Inference
/// Binds identifiers to symbols and infers types for all expressions.
/// </summary>
public class BindingPass : ICompilerPass
{
    public string Name => "Binding & Type Inference";
    public string Description => "Bind identifiers and infer types";
    public int Order => 47; 
    
    public bool Execute(CompilationContext context)
    {
        if (context.Model == null) return false;
        
        var binder = new ExpressionBinder(context);
        
        // 1. Bind Entities (Computed Fields)
        foreach (var entity in context.Model.Entities)
        {
            foreach (var field in entity.Fields)
            {
                if (field.ComputedExpr != null)
                {
                    binder.CurrentContext = entity;
                    binder.BindAndInfer(field.ComputedExpr);
                }
            }
        }
        
        // 2. Bind Rules
        foreach (var rule in context.Model.Rules)
        {
            // Set context to target entity
            if (context.Symbols.TryResolve(rule.TargetEntity, out var sym) && sym?.Element is BmEntity entity)
            {
                binder.CurrentContext = entity;
            }
            
            foreach (var stmt in rule.Statements)
            {
                BindStatement(stmt, binder);
            }
        }
        
        // 3. Bind Access Controls
        foreach (var ac in context.Model.AccessControls)
        {
             if (context.Symbols.TryResolve(ac.TargetEntity, out var sym) && sym?.Element is BmEntity entity)
             {
                 binder.CurrentContext = entity;
                 foreach (var policy in ac.Rules)
                 {
                     if (policy.WhereConditionExpr != null)
                        binder.BindAndInfer(policy.WhereConditionExpr);
                        
                     // Field restrictions might also have conditions
                     foreach (var f in policy.FieldRestrictions)
                     {
                         if (f.ConditionExpr != null)
                            binder.BindAndInfer(f.ConditionExpr);
                     }
                 }
             }
        }
        
        context.BoundIdentifiers = binder.BoundIds;
        context.TypeInferenceCount = binder.InferredCount;
        
        return !context.HasErrors;
    }
    private void BindStatement(BmRuleStatement stmt, ExpressionBinder binder)
    {
        if (stmt is BmValidateStatement val && val.ExpressionAst != null)
        {
            binder.BindAndInfer(val.ExpressionAst);
        }
        else if (stmt is BmComputeStatement comp && comp.ExpressionAst != null)
        {
            binder.BindAndInfer(comp.ExpressionAst);
        }
        else if (stmt is BmWhenStatement when)
        {
            if (when.ConditionAst != null)
                binder.BindAndInfer(when.ConditionAst);
                
            foreach (var thenStmt in when.ThenStatements)
                BindStatement(thenStmt, binder);
                
            foreach (var elseStmt in when.ElseStatements)
                BindStatement(elseStmt, binder);
        }
        else if (stmt is BmCallStatement call)
        {
            foreach (var arg in call.Arguments)
                binder.BindAndInfer(arg);
        }
        else if (stmt is BmEmitStatement emit)
        {
            foreach (var assignment in emit.FieldAssignments.Values)
                binder.BindAndInfer(assignment);
        }
        else if (stmt is BmReturnStatement ret && ret.ExpressionAst != null)
        {
            binder.BindAndInfer(ret.ExpressionAst);
        }
        else if (stmt is BmLetStatement let && let.ExpressionAst != null)
        {
            binder.BindAndInfer(let.ExpressionAst);
        }
        else if (stmt is BmRejectStatement reject && reject.Message != null)
        {
            binder.BindAndInfer(reject.Message);
        }
        else if (stmt is BmForeachStatement forEach)
        {
            if (forEach.CollectionAst != null)
                binder.BindAndInfer(forEach.CollectionAst);
                
            foreach (var bodyStmt in forEach.Body)
                BindStatement(bodyStmt, binder);
        }
    }
}

public class ExpressionBinder
{
    private readonly CompilationContext _context;
    public BmEntity? CurrentContext { get; set; }
    public int BoundIds { get; private set; }
    public int InferredCount { get; private set; }
    
    public ExpressionBinder(CompilationContext context)
    {
        _context = context;
    }
    
    public void BindAndInfer(BmExpression expr)
    {
        Bind(expr);
        Infer(expr);
    }
    
    private void Bind(BmExpression expr)
    {
        ExpressionTraversalUtility.Traverse(expr, node =>
        {
            if (node is BmIdentifierExpression id)
                BindIdentifier(id);
        });
    }
    
    private void BindIdentifier(BmIdentifierExpression id)
    {
        if (CurrentContext == null) return;
        
        // Simple case: Field in current entity
        if (id.IsSimple)
        {
            var fieldName = id.Root;
            
            // Check fields
            var field = CurrentContext.Fields.FirstOrDefault(f => f.Name == fieldName);
            if (field != null)
            {
                id.BoundSymbol = field;
                BoundIds++;
                return;
            }
            
            // Check associations
            var assoc = CurrentContext.Associations.FirstOrDefault(a => a.Name == fieldName);
            if (assoc != null)
            {
                id.BoundSymbol = assoc;
                BoundIds++;
                return;
            }
        }
        
        // Multi-part path navigation: e.g. customer.name, order.customer.name
        if (id.Path.Count > 1)
        {
            BindPathNavigation(id);
        }
    }

    /// <summary>
    /// Resolve multi-part expression paths across association/composition chains.
    /// Example: order.customer.name → walk Order→Customer association, resolve 'name' field on Customer.
    /// </summary>
    private void BindPathNavigation(BmIdentifierExpression id)
    {
        if (CurrentContext == null || _context.Model == null) return;

        var currentEntity = CurrentContext;

        // Walk intermediate segments (all except the last) — each must be an association or composition
        for (int i = 0; i < id.Path.Count - 1; i++)
        {
            var segmentName = id.Path[i];

            // Look for association in current entity
            var assoc = currentEntity.Associations.FirstOrDefault(a =>
                a.Name.Equals(segmentName, StringComparison.OrdinalIgnoreCase));

            // Also check compositions (BmComposition extends BmAssociation)
            if (assoc == null)
            {
                assoc = currentEntity.Compositions.FirstOrDefault(c =>
                    c.Name.Equals(segmentName, StringComparison.OrdinalIgnoreCase));
            }

            if (assoc == null)
            {
                _context.AddError(ErrorCodes.BIND_UNRESOLVED_ASSOCIATION,
                    $"Cannot resolve association '{segmentName}' in path '{id.FullPath}' on entity '{currentEntity.Name}'",
                    id.SourceFile, id.StartLine, "Binding & Type Inference");
                return;
            }

            // Resolve the target entity from the symbol table
            var targetEntity = ResolveTargetEntity(assoc.TargetEntity);
            if (targetEntity == null)
            {
                _context.AddError(ErrorCodes.BIND_UNRESOLVED_TARGET_ENTITY,
                    $"Cannot resolve target entity '{assoc.TargetEntity}' for association '{segmentName}' in path '{id.FullPath}'",
                    id.SourceFile, id.StartLine, "Binding & Type Inference");
                return;
            }

            currentEntity = targetEntity;
        }

        // Final segment must be a field in the resolved target entity
        var finalSegment = id.Path[id.Path.Count - 1];
        var field = currentEntity.Fields.FirstOrDefault(f =>
            f.Name.Equals(finalSegment, StringComparison.OrdinalIgnoreCase));

        if (field != null)
        {
            id.BoundSymbol = field;
            id.InferredType = new BmCustomTypeReference(field.TypeString);
            BoundIds++;
            InferredCount++;
            return;
        }

        // Also check if final segment is an association (e.g., order.customer returns entity ref)
        var finalAssoc = currentEntity.Associations.FirstOrDefault(a =>
            a.Name.Equals(finalSegment, StringComparison.OrdinalIgnoreCase));
        if (finalAssoc == null)
        {
            finalAssoc = currentEntity.Compositions.FirstOrDefault(c =>
                c.Name.Equals(finalSegment, StringComparison.OrdinalIgnoreCase));
        }

        if (finalAssoc != null)
        {
            id.BoundSymbol = finalAssoc;
            id.InferredType = new BmEntityTypeReference(finalAssoc.TargetEntity);
            BoundIds++;
            InferredCount++;
            return;
        }

        _context.AddError(ErrorCodes.BIND_UNRESOLVED_PATH_FIELD,
            $"Cannot resolve field '{finalSegment}' on entity '{currentEntity.Name}' in path '{id.FullPath}'",
            id.SourceFile, id.StartLine, "Binding & Type Inference");
    }

    /// <summary>
    /// Resolve a target entity name through the symbol table, trying multiple resolution strategies.
    /// </summary>
    private BmEntity? ResolveTargetEntity(string targetEntityName)
    {
        // Try exact match first
        if (_context.Symbols.TryResolve(targetEntityName, out var sym) && sym?.Element is BmEntity entity)
            return entity;

        // Try with model namespace
        if (_context.Model?.Namespace != null)
        {
            var qualified = $"{_context.Model.Namespace}.{targetEntityName}";
            if (_context.Symbols.TryResolve(qualified, out var nsym) && nsym?.Element is BmEntity nEntity)
                return nEntity;
        }

        // Fallback: search model entities directly (handles both Name and QualifiedName)
        return _context.Model?.FindEntity(targetEntityName);
    }
    
    private void Infer(BmExpression expr)
    {
        if (expr.InferredType != null) return; // Already inferred
        
        if (expr is BmLiteralExpression lit)
        {
            expr.InferredType = InferLiteral(lit);
        }
        else if (expr is BmIdentifierExpression id)
        {
            if (id.BoundSymbol is BmField field)
            {
                // Parse the type string to create a reference
                // Ideally we should reuse the parsed type from field if available, but field.TypeString is what we have mostly used
                // The parser creates BmTypeReference but it might rely on field.TypeRef if that exists? 
                // BmField usually has TypeString. Let's create a custom type ref for now.
                // Or better, check if field has a parsed TypeReference.
                // Assuming field has TypeRef. If not, use TypeString.
                // BmField definition: let's assume it has TypeString.
                expr.InferredType = new BmCustomTypeReference(field.TypeString);
            }
            else if (id.BoundSymbol is BmAssociation assoc)
            {
                // Association type is the target entity
                expr.InferredType = new BmEntityTypeReference(assoc.TargetEntity);
            }
        }
        else if (expr is BmBinaryExpression bin)
        {
            Infer(bin.Left);
            Infer(bin.Right);
            // Logic to determine result type from operands
            // E.g. int + int = int
            if (bin.Left.InferredType != null)
                expr.InferredType = bin.Left.InferredType; // Naive assumption for now
        }
        
        if (expr.InferredType != null)
            InferredCount++;
    }
    
    private BmTypeReference InferLiteral(BmLiteralExpression lit)
    {
        return lit.Kind switch
        {
            BmLiteralKind.String => BmPrimitiveType.String(),
            BmLiteralKind.Integer => BmPrimitiveType.Integer(),
            BmLiteralKind.Decimal => BmPrimitiveType.Decimal(),
            BmLiteralKind.Boolean => BmPrimitiveType.Boolean(),
            _ => BmPrimitiveType.String()
        };
    }
}
