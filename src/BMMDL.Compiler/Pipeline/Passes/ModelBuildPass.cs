using BMMDL.MetaModel;
using BMMDL.Compiler.Parsing;
using BMMDL.MetaModel.Expressions;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 3: Model Building
/// Converts parse trees to BmModel with expression ASTs.
/// </summary>
public class ModelBuildPass : ICompilerPass
{
    public string Name => "Model Building";
    public string Description => "Build meta model from parse trees";
    public int Order => 3;
    
    public bool Execute(CompilationContext context)
    {
        var mergedModel = new BmModel();
        int expressionNodes = 0;
        bool success = true;
        
        foreach (var (file, tree) in context.ParseTrees)
        {
            try
            {
                var builder = new BmmdlModelBuilder(file);
                var fileModel = builder.Visit(tree as Parsing.BmmdlParser.CompilationUnitContext);
                
                // Merge into main model
                if (fileModel != null)
                {
                    MergeModel(mergedModel, fileModel);
                    
                    // Count expression nodes
                    expressionNodes += CountExpressionNodes(fileModel);
                }
            }
            catch (Exception ex)
            {
                context.AddError(ErrorCodes.MOD_BUILD_ERROR, $"Model build error: {ex.Message}", file, pass: Name);
                success = false;
            }
        }
        
        // Validate we got something
        if (mergedModel.Entities.Count == 0 && mergedModel.Types.Count == 0 && mergedModel.Services.Count == 0)
        {
            context.AddWarning(ErrorCodes.MOD_NO_ELEMENTS, "No model elements found in source files", pass: Name);
        }
        
        context.Model = mergedModel;
        context.ExpressionNodeCount = expressionNodes;
        
        return success;
    }
    
    private void MergeModel(BmModel target, BmModel source)
    {
        // Collect all module declarations
        if (source.Module != null)
        {
            target.AllModules.Add(source.Module);
        }
        target.AllModules.AddRange(source.AllModules);
        
        // Preserve primary module declaration (first one wins)
        if (target.Module == null && source.Module != null)
        {
            target.Module = source.Module;
        }
        
        // Preserve namespace
        if (string.IsNullOrEmpty(target.Namespace) && !string.IsNullOrEmpty(source.Namespace))
        {
            target.Namespace = source.Namespace;
        }
        
        target.Entities.AddRange(source.Entities);
        target.Types.AddRange(source.Types);
        target.Enums.AddRange(source.Enums);
        target.Aspects.AddRange(source.Aspects);
        target.Services.AddRange(source.Services);
        target.Rules.AddRange(source.Rules);
        target.AccessControls.AddRange(source.AccessControls);
        target.Sequences.AddRange(source.Sequences);
        target.Views.AddRange(source.Views);
        target.Events.AddRange(source.Events);
        target.Extensions.AddRange(source.Extensions);
        target.Modifications.AddRange(source.Modifications);
        target.AnnotateDirectives.AddRange(source.AnnotateDirectives);
        target.Migrations.AddRange(source.Migrations);
    }
    
    private int CountExpressionNodes(BmModel model)
    {
        int count = 0;
        
        foreach (var entity in model.Entities)
        {
            foreach (var field in entity.Fields)
            {
                if (field.ComputedExpr != null)
                    count += CountNodes(field.ComputedExpr);
                if (field.DefaultExpr != null)
                    count += CountNodes(field.DefaultExpr);
            }
            foreach (var assoc in entity.Associations)
            {
                if (assoc.OnConditionExpr != null)
                    count += CountNodes(assoc.OnConditionExpr);
            }
        }
        
        foreach (var rule in model.Rules)
        {
            foreach (var stmt in rule.Statements)
            {
                count += CountStatementExpressions(stmt);
            }
        }
        
        return count;
    }
    
    private int CountNodes(BmExpression expr)
    {
        int count = 0;
        BmExpressionWalker.Walk(expr, _ => count++);
        return count;
    }
    
    private int CountStatementExpressions(BmRuleStatement stmt)
    {
        int count = 0;
        
        switch (stmt)
        {
            case BmValidateStatement v:
                if (v.ExpressionAst != null)
                    count += CountNodes(v.ExpressionAst);
                break;
            case BmComputeStatement c:
                if (c.ExpressionAst != null)
                    count += CountNodes(c.ExpressionAst);
                break;
            case BmWhenStatement w:
                if (w.ConditionAst != null)
                    count += CountNodes(w.ConditionAst);
                foreach (var child in w.ThenStatements)
                    count += CountStatementExpressions(child);
                foreach (var child in w.ElseStatements)
                    count += CountStatementExpressions(child);
                break;
        }
        
        return count;
    }
}
