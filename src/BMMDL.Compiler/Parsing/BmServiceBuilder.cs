using BMMDL.MetaModel;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Expressions;

namespace BMMDL.Compiler.Parsing;

/// <summary>
/// Builds service-related model elements: services, actions, functions, views/tables,
/// rules, events, and sequences.
/// </summary>
public class BmServiceBuilder : BuilderBase
{
    private readonly BmEntityElementBuilder _elemBuilder;
    private readonly BmExpressionBuilder _exprBuilder;
    private readonly BmStatementBuilder _stmtBuilder;
    private readonly BmTypeReferenceBuilder _typeBuilder = new();

    public BmServiceBuilder(
        string? sourceFile,
        List<ParseDiagnostic> diagnostics,
        BmEntityElementBuilder elemBuilder,
        BmExpressionBuilder exprBuilder,
        BmStatementBuilder stmtBuilder)
        : base(sourceFile, diagnostics, "ServiceBuilder")
    {
        _elemBuilder = elemBuilder;
        _exprBuilder = exprBuilder;
        _stmtBuilder = stmtBuilder;
    }

    public BmService VisitServiceDef(BmmdlParser.ServiceDefContext context, List<BmAnnotation> annotations,
        string currentNamespace)
    {
        var service = new BmService
        {
            Name = context.IDENTIFIER().GetText(),
            Namespace = currentNamespace,
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        // FOR clause: service MyService for MyEntity { ... }
        if (context.FOR() != null && context.identifierReference() != null)
        {
            service.ForEntity = context.identifierReference().GetText();
        }

        foreach (var elem in context.serviceElement())
        {
            var elemAnnotations = elem.annotation().Select(_elemBuilder.VisitAnnotation).ToList();

            if (elem.entityExposure() != null)
            {
                var exposure = elem.entityExposure();
                var entity = new BmEntity
                {
                    Name = exposure.IDENTIFIER().GetText(),
                    SourceFile = _sourceFile,
                    StartLine = exposure.Start.Line,
                    EndLine = exposure.Stop.Line
                };
                entity.Annotations.AddRange(elemAnnotations);
                entity.Aspects.Add(exposure.identifierReference().GetText()); // Source entity

                // Parse projection clause if present
                var projection = exposure.projectionClause();
                if (projection != null)
                {
                    if (projection.STAR() != null)
                    {
                        // Wildcard '*' — include all fields, possibly with exclusions
                        var excluding = projection.excludingClause();
                        if (excluding != null)
                        {
                            entity.ExcludeFields = excluding.IDENTIFIER()
                                .Select(id => id.GetText())
                                .ToList();
                        }
                    }
                    else if (projection.projectionItem().Length > 0)
                    {
                        // Explicit field list
                        entity.IncludeFields = projection.projectionItem()
                            .Select(item => item.IDENTIFIER().Length > 0
                                ? item.IDENTIFIER(0).GetText()
                                : item.identifierReference()?.GetText() ?? "")
                            .Where(name => !string.IsNullOrEmpty(name))
                            .ToList();
                    }
                }

                service.Entities.Add(entity);
            }
            else if (elem.functionDef() != null)
            {
                var func = VisitFunctionDef(elem.functionDef());
                func.Annotations.AddRange(elemAnnotations);
                service.Functions.Add(func);
            }
            else if (elem.actionDef() != null)
            {
                var action = VisitActionDef(elem.actionDef());
                action.Annotations.AddRange(elemAnnotations);
                service.Actions.Add(action);
            }
            else if (elem.serviceEventHandler() != null)
            {
                var handler = VisitServiceEventHandler(elem.serviceEventHandler());
                if (handler != null)
                    service.EventHandlers.Add(handler);
            }
        }

        service.Annotations.AddRange(annotations);
        return service;
    }

    public BmEventHandler VisitServiceEventHandler(BmmdlParser.ServiceEventHandlerContext context)
    {
        var handler = new BmEventHandler
        {
            EventName = context.identifierReference().GetText(),
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        foreach (var stmt in context.actionStmt())
        {
            handler.Statements.Add(_stmtBuilder.BuildActionStmt(stmt));
        }

        return handler;
    }

    public BmFunction VisitFunctionDef(BmmdlParser.FunctionDefContext context)
        => ActionFunctionParsingHelper.ParseFunction(context, _elemBuilder, _stmtBuilder, _sourceFile);

    public BmAction VisitActionDef(BmmdlParser.ActionDefContext context)
        => ActionFunctionParsingHelper.ParseAction(context, _elemBuilder, _exprBuilder, _stmtBuilder, _sourceFile, AddParseWarning, AddParseWarning);

    public BmView VisitTableDef(BmmdlParser.TableDefContext context, List<BmAnnotation> annotations,
        string currentNamespace)
    {
        var view = new BmView
        {
            Name = context.IDENTIFIER().GetText(),
            Namespace = currentNamespace,
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        if (context.selectStatement() != null)
        {
            // Always keep raw string for backward compatibility
            view.SelectStatement = context.selectStatement().GetText();

            // Parse into AST (graceful fallback: raw string preserved if this fails)
            try
            {
                var selectBuilder = new BmSelectStatementBuilder(_exprBuilder, _sourceFile, _diagnostics);
                view.ParsedSelect = selectBuilder.Build(context.selectStatement());
            }
            catch (Exception ex)
            {
                AddParseWarning(context.Start.Line, "ViewSelectAST",
                    $"Failed to parse view SELECT into AST for '{view.Name}'", ex);
            }
        }
        else if (context.projectionDef() != null)
        {
            BuildProjection(view, context.projectionDef());
        }

        if (context.viewParams() != null)
        {
            foreach (var param in context.viewParams().viewParam())
            {
                var viewParam = new BmViewParameter
                {
                    Name = param.IDENTIFIER().GetText(),
                    Type = param.typeReference().GetText(),
                    DefaultValue = param.expression()?.GetText(),
                    SourceFile = _sourceFile,
                    StartLine = param.Start.Line,
                    EndLine = param.Stop.Line
                };
                if (param.expression() != null)
                {
                    try { viewParam.DefaultExpr = _exprBuilder.Visit(param.expression()); }
                    catch (Exception ex) { AddParseWarning(param.Start.Line, "ViewParamDefault", $"Failed to parse view parameter default expression", ex); }
                }
                view.Parameters.Add(viewParam);
            }
        }

        view.Annotations.AddRange(annotations);
        return view;
    }

    public void BuildProjection(BmView view, BmmdlParser.ProjectionDefContext projectionCtx)
    {
        view.IsProjection = true;
        view.ProjectionEntityName = projectionCtx.identifierReference().GetText();

        var selectFields = new List<string>();

        foreach (var field in projectionCtx.projectionField())
        {
            if (field.STAR() != null)
            {
                // Wildcard: * or * excluding (field1, field2)
                view.ProjectionIncludesAll = true;

                var excluding = field.fieldRefList();
                if (excluding != null)
                {
                    foreach (var id in excluding.IDENTIFIER())
                    {
                        view.ExcludedFields.Add(id.GetText());
                    }
                }

                if (view.ExcludedFields.Count == 0)
                {
                    selectFields.Add("*");
                }
                // If excluding, defer SELECT generation — SymbolResolutionPass will expand
            }
            else
            {
                // Explicit field: identifierReference (AS alias)?
                var fieldName = field.identifierReference().GetText();
                var alias = field.IDENTIFIER()?.GetText();

                view.ProjectionFields.Add(new BmProjectionField
                {
                    FieldName = fieldName,
                    Alias = alias
                });

                selectFields.Add(alias != null ? $"{fieldName} AS {alias}" : fieldName);
            }
        }

        // Generate SELECT statement for non-excluding projections
        if (view.ExcludedFields.Count == 0)
        {
            var columns = selectFields.Count > 0 ? string.Join(", ", selectFields) : "*";
            view.SelectStatement = $"SELECT {columns} FROM {view.ProjectionEntityName}";
        }
        // For * excluding: SelectStatement stays empty until SymbolResolutionPass resolves it
    }

    public BmRule VisitRuleDef(BmmdlParser.RuleDefContext context, List<BmAnnotation> annotations)
    {
        var rule = new BmRule
        {
            Name = context.IDENTIFIER().GetText(),
            // FOR identifierReference is optional (omitted inside aspects)
            TargetEntity = context.identifierReference()?.GetText() ?? "",
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        foreach (var trigger in context.triggerEvent())
        {
            rule.Triggers.Add(VisitTriggerEvent(trigger));
        }

        foreach (var stmt in context.ruleStmt())
        {
            rule.Statements.Add(_stmtBuilder.BuildRuleStmt(stmt));
        }

        rule.Annotations.AddRange(annotations);
        return rule;
    }

    public BmTriggerEvent VisitTriggerEvent(BmmdlParser.TriggerEventContext context)
    {
        var trigger = new BmTriggerEvent();

        if (context.BEFORE() != null)
        {
            trigger.Timing = BmTriggerTiming.Before;
        }
        else if (context.AFTER() != null)
        {
            trigger.Timing = BmTriggerTiming.After;
        }
        else if (context.CHANGE() != null)
        {
            trigger.Timing = BmTriggerTiming.OnChange;
            trigger.ChangeFields.AddRange(context.IDENTIFIER().Select(i => i.GetText()));
            return trigger;
        }

        if (context.CREATE() != null) trigger.Operation = BmTriggerOperation.Create;
        else if (context.UPDATE() != null) trigger.Operation = BmTriggerOperation.Update;
        else if (context.DELETE() != null) trigger.Operation = BmTriggerOperation.Delete;
        else if (context.READ() != null) trigger.Operation = BmTriggerOperation.Read;

        return trigger;
    }

    public BmSequence VisitSequenceDef(BmmdlParser.SequenceDefContext context, List<BmAnnotation> annotations)
    {
        var seq = new BmSequence
        {
            Name = context.IDENTIFIER(0).GetText(),
            ForEntity = context.identifierReference()?.GetText(),
            ForField = context.IDENTIFIER().Length > 1 ? context.IDENTIFIER(1).GetText() : null,
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        foreach (var prop in context.sequenceProp())
        {
            if (prop.PATTERN() != null)
                seq.Pattern = prop.STRING_LITERAL().GetText().Trim('\'');
            else if (prop.START() != null)
                seq.StartValue = int.TryParse(prop.INTEGER_LITERAL().GetText(), out var startVal) ? startVal : 1;
            else if (prop.INCREMENT() != null)
                seq.Increment = int.TryParse(prop.INTEGER_LITERAL().GetText(), out var incVal) ? incVal : 1;
            else if (prop.PADDING() != null)
                seq.Padding = int.TryParse(prop.INTEGER_LITERAL().GetText(), out var padVal) ? padVal : 0;
            else if (prop.MAX() != null)
                seq.MaxValue = int.TryParse(prop.INTEGER_LITERAL().GetText(), out var maxVal) ? maxVal : int.MaxValue;
            else if (prop.SCOPE() != null)
            {
                var scope = prop.scopeLevel();
                seq.Scope = scope.GLOBAL() != null ? BmSequenceScope.Global
                          : scope.TENANT() != null ? BmSequenceScope.Tenant
                          : BmSequenceScope.Company;
            }
            else if (prop.RESET() != null)
            {
                var reset = prop.resetTrigger();
                seq.ResetOn = reset.NEVER() != null ? BmResetTrigger.Never
                            : reset.DAILY() != null ? BmResetTrigger.Daily
                            : reset.MONTHLY() != null ? BmResetTrigger.Monthly
                            : reset.YEARLY() != null ? BmResetTrigger.Yearly
                            : reset.FISCAL() != null ? BmResetTrigger.FiscalYear
                            : BmResetTrigger.Never;
            }
        }

        seq.Annotations.AddRange(annotations);
        return seq;
    }

    public BmEvent VisitEventDef(BmmdlParser.EventDefContext context, List<BmAnnotation> annotations,
        BmModel model)
    {
        var evt = new BmEvent
        {
            Name = context.IDENTIFIER().GetText(),
            Namespace = model.Namespace ?? "",
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };
        
        foreach (var fieldCtx in context.eventField())
        {
            var field = new BmEventField
            {
                Name = fieldCtx.IDENTIFIER().GetText(),
                TypeString = fieldCtx.typeReference().GetText()
            };
            
            try
            {
                field.TypeRef = _typeBuilder.Parse(field.TypeString);
            }
            catch (Exception ex) { AddParseWarning(fieldCtx.Start.Line, "EventFieldType", $"Failed to parse type '{field.TypeString}'", ex); }
            
            // Event field annotations
            foreach (var annCtx in fieldCtx.annotation())
            {
                field.Annotations.Add(_elemBuilder.VisitAnnotation(annCtx));
            }
            
            evt.Fields.Add(field);
        }
        
        evt.Annotations.AddRange(annotations);
        return evt;
    }
}
