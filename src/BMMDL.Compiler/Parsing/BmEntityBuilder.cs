using BMMDL.MetaModel;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Enums;

namespace BMMDL.Compiler.Parsing;

/// <summary>
/// Builds entity-related model elements: entities, aspects, types, enums, extensions, and modifications.
/// </summary>
public class BmEntityBuilder : BuilderBase
{
    private readonly BmEntityElementBuilder _elemBuilder;
    private readonly BmExpressionBuilder _exprBuilder;
    private readonly BmStatementBuilder _stmtBuilder;

    public BmEntityBuilder(
        string? sourceFile,
        List<ParseDiagnostic> diagnostics,
        BmEntityElementBuilder elemBuilder,
        BmExpressionBuilder exprBuilder,
        BmStatementBuilder stmtBuilder)
        : base(sourceFile, diagnostics, "EntityBuilder")
    {
        _elemBuilder = elemBuilder;
        _exprBuilder = exprBuilder;
        _stmtBuilder = stmtBuilder;
    }

    public BmEntity VisitEntityDef(BmmdlParser.EntityDefContext context, List<BmAnnotation> annotations,
        string currentNamespace, BmModel model)
    {
        var entity = new BmEntity
        {
            Name = context.IDENTIFIER().GetText(),
            Namespace = currentNamespace,
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        // Abstract flag
        if (context.ABSTRACT() != null)
        {
            entity.IsAbstract = true;
        }

        // EXTENDS parent entity (table-per-type inheritance)
        if (context.EXTENDS() != null)
        {
            // The first identifierReference after EXTENDS is the parent entity
            var refs = context.identifierReference();
            if (refs.Length > 0)
            {
                entity.ParentEntityName = refs[0].GetText();
                entity.DiscriminatorValue = entity.Name;
                
                // Remaining refs after the parent (after COLON) are aspects
                for (int i = 1; i < refs.Length; i++)
                {
                    entity.Aspects.Add(refs[i].GetText());
                }
            }
        }
        else
        {
            // No EXTENDS — all identifierReferences after COLON are aspects
            var refs = context.identifierReference();
            foreach (var refCtx in refs)
            {
                entity.Aspects.Add(refCtx.GetText());
            }
        }

        // Annotations
        entity.Annotations.AddRange(annotations);

        // Determine tenant-scoping
        // Priority: 1) @TenantScoped annotation, 2) TenantAware aspect, 3) Module.TenantAware
        if (annotations.Any(a => a.Name == "TenantScoped"))
        {
            entity.TenantScoped = true;
        }
        else if (annotations.Any(a => a.Name == "GlobalScoped"))
        {
            entity.TenantScoped = false;
        }
        else if (entity.Aspects.Any(a => a == "TenantAware" || a.EndsWith(".TenantAware")))
        {
            entity.TenantScoped = true;
        }
        else if (model.Module?.TenantAware == true)
        {
            // Inherit from module-level configuration
            entity.TenantScoped = true;
        }

        // Elements
        foreach (var elem in context.entityElement())
        {
            VisitEntityElement(elem, entity);
        }

        return entity;
    }

    public void VisitEntityElement(BmmdlParser.EntityElementContext context, BmEntity entity)
    {
        var elemAnnotations = context.annotation().Select(_elemBuilder.VisitAnnotation).ToList();

        if (context.fieldDef() != null)
        {
            var field = _elemBuilder.VisitFieldDef(context.fieldDef(), context.keyElement() != null);
            field.Annotations.AddRange(elemAnnotations);

            // Parse @Computed.Strategy annotation
            var strategyAnnot = elemAnnotations.FirstOrDefault(a => a.Name == "Computed.Strategy");
            if (strategyAnnot?.Value != null)
            {
                var strategyStr = strategyAnnot.Value.ToString();
                if (strategyStr != null && strategyStr.StartsWith("#"))
                {
                    strategyStr = strategyStr.Substring(1); // Remove #
                    if (Enum.TryParse<ComputedStrategy>(strategyStr, true, out var strategy))
                    {
                        field.ComputedStrategy = strategy;
                    }
                }
            }

            // Parse FileReference storage annotations
            if (field.TypeRef is BmFileReferenceType fileRefType)
            {
                // @Storage.Provider: 'S3'
                var providerAnnot = elemAnnotations.FirstOrDefault(a => a.Name == "Storage.Provider");
                if (providerAnnot?.Value != null)
                {
                    fileRefType.Provider = providerAnnot.Value.ToString()?.Trim('\'', '"');
                }

                // @Storage.Bucket: 'bucket-name'
                var bucketAnnot = elemAnnotations.FirstOrDefault(a => a.Name == "Storage.Bucket");
                if (bucketAnnot?.Value != null)
                {
                    fileRefType.BucketName = bucketAnnot.Value.ToString()?.Trim('\'', '"');
                }

                // @Storage.MaxSize: 52428800
                var maxSizeAnnot = elemAnnotations.FirstOrDefault(a => a.Name == "Storage.MaxSize");
                if (maxSizeAnnot?.Value != null && long.TryParse(maxSizeAnnot.Value.ToString(), out var maxSize))
                {
                    fileRefType.MaxSizeBytes = maxSize;
                }

                // @Storage.AllowedTypes: ['application/pdf', 'image/jpeg']
                var allowedTypesAnnot = elemAnnotations.FirstOrDefault(a => a.Name == "Storage.AllowedTypes");
                if (allowedTypesAnnot?.Value is List<object> allowedTypesList)
                {
                    fileRefType.AllowedMimeTypes = allowedTypesList
                        .Select(v => v.ToString()?.Trim('\'', '"'))
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Select(v => v!)
                        .ToList();
                }
            }

            entity.Fields.Add(field);
        }
        else if (context.keyElement() != null)
        {
            var field = _elemBuilder.VisitFieldDef(context.keyElement().fieldDef(), true);
            field.Annotations.AddRange(elemAnnotations);
            entity.Fields.Add(field);
        }
        else if (context.associationDef() != null)
        {
            var assoc = _elemBuilder.VisitAssociationDef(context.associationDef());
            assoc.Annotations.AddRange(elemAnnotations);
            entity.Associations.Add(assoc);
        }
        else if (context.compositionDef() != null)
        {
            var comp = _elemBuilder.VisitCompositionDef(context.compositionDef());
            comp.Annotations.AddRange(elemAnnotations);
            entity.Compositions.Add(comp);
        }
        // Phase 7: Index definitions
        else if (context.indexDef() != null)
        {
            var idx = _elemBuilder.VisitIndexDef(context.indexDef());
            entity.Indexes.Add(idx);
        }
        // Phase 7: Constraint definitions
        else if (context.constraintDef() != null)
        {
            var constraint = _elemBuilder.VisitConstraintDef(context.constraintDef());
            entity.Constraints.Add(constraint);
        }
        // Phase 8: Bounded actions (OData v4)
        else if (context.actionDef() != null)
        {
            try 
            {
                var action = VisitActionDef(context.actionDef());
                action.Annotations.AddRange(elemAnnotations);
                entity.BoundActions.Add(action);
            }
            catch (Exception ex)
            {
                AddParseWarning(context.Start.Line, "BoundAction", $"Failed to parse bounded action: {ex.Message}");
            }
        }
        // Phase 9: Bounded functions (OData v4)
        else if (context.functionDef() != null)
        {
            try 
            {
                var function = VisitFunctionDef(context.functionDef());
                function.Annotations.AddRange(elemAnnotations);
                entity.BoundFunctions.Add(function);
            }
            catch (Exception ex)
            {
                AddParseWarning(context.Start.Line, "BoundFunction", $"Failed to parse bounded function: {ex.Message}");
            }
        }
    }

    public BmType VisitTypeDef(BmmdlParser.TypeDefContext context, List<BmAnnotation> annotations,
        string currentNamespace)
    {
        var type = new BmType
        {
            Name = context.IDENTIFIER().GetText(),
            Namespace = currentNamespace,
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        // Check for base type (type alias)
        if (context.typeReference() != null)
        {
            type.BaseType = context.typeReference().GetText();
        }

        // Parse fields (structured type via structDef)
        if (context.structDef() != null)
        {
            foreach (var fieldCtx in context.structDef().structField())
            {
                var elemAnnotations = fieldCtx.annotation().Select(_elemBuilder.VisitAnnotation).ToList();
                var field = new BmField
                {
                    Name = fieldCtx.IDENTIFIER().GetText(),
                    TypeString = fieldCtx.typeReference().GetText(),
                    SourceFile = _sourceFile,
                    StartLine = fieldCtx.Start.Line,
                    EndLine = fieldCtx.Stop.Line
                };
                try
                {
                    field.TypeRef = new BmTypeReferenceBuilder().Parse(field.TypeString);
                }
                catch (Exception ex)
                {
                    // Type resolution is intentionally deferred to later compiler passes
                    // (SymbolResolutionPass, BindingPass). Parse failure here is expected
                    // for user-defined types that haven't been registered yet.
                    AddParseWarning(fieldCtx.Start.Line, "TypeReference",
                        $"Deferred type resolution for '{field.TypeString}': {ex.Message}");
                }
                field.Annotations.AddRange(elemAnnotations);
                type.Fields.Add(field);
            }
        }

        type.Annotations.AddRange(annotations);
        return type;
    }

    public BmEnum VisitEnumDef(BmmdlParser.EnumDefContext context, List<BmAnnotation> annotations,
        string currentNamespace)
    {
        var enumDef = new BmEnum
        {
            Name = context.IDENTIFIER().GetText(),
            Namespace = currentNamespace,
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        foreach (var val in context.enumValue())
        {
            var enumVal = new BmEnumValue
            {
                Name = val.IDENTIFIER().GetText(),
                Value = val.literal() != null ? _elemBuilder.ExtractLiteral(val.literal()) : null,
                SourceFile = _sourceFile,
                StartLine = val.Start.Line,
                EndLine = val.Stop.Line
            };
            enumVal.Annotations.AddRange(val.annotation().Select(_elemBuilder.VisitAnnotation));
            enumDef.Values.Add(enumVal);
        }

        enumDef.Annotations.AddRange(annotations);
        return enumDef;
    }

    public BmAspect VisitAspectDef(BmmdlParser.AspectDefContext context, List<BmAnnotation> annotations,
        string currentNamespace,
        Func<BmmdlParser.RuleDefContext, List<BmAnnotation>, BmRule> visitRuleDef,
        Func<BmmdlParser.AccessControlDefContext, BmAccessControl> visitAccessControlDef)
    {
        var aspect = new BmAspect
        {
            Name = context.IDENTIFIER().GetText(),
            Namespace = currentNamespace,
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        // Includes
        foreach (var inc in context.identifierReference())
        {
            aspect.Includes.Add(inc.GetText());
        }

        // Aspect elements (fields, associations, compositions, rules, access controls)
        foreach (var elem in context.aspectElement())
        {
            // ruleDef and accessControlDef are direct children of aspectElement
            if (elem.ruleDef() != null)
            {
                var elemAnnotations = elem.annotation().Select(_elemBuilder.VisitAnnotation).ToList();
                var rule = visitRuleDef(elem.ruleDef(), elemAnnotations);
                // Leave TargetEntity empty — resolved at inline time
                rule.TargetEntity = "";
                aspect.Rules.Add(rule);
            }
            else if (elem.accessControlDef() != null)
            {
                var acl = visitAccessControlDef(elem.accessControlDef());
                // Leave TargetEntity empty — resolved at inline time
                acl.TargetEntity = "";
                aspect.AccessControls.Add(acl);
            }
            else
            {
                // Structural elements: fieldDef, associationDef, compositionDef, keyElement, etc.
                var elemAnnotations = elem.annotation().Select(_elemBuilder.VisitAnnotation).ToList();
                
                if (elem.fieldDef() != null || elem.keyElement() != null)
                {
                    var fieldCtx = elem.fieldDef() ?? elem.keyElement()?.fieldDef();
                    if (fieldCtx != null)
                    {
                        var field = _elemBuilder.VisitFieldDef(fieldCtx, elem.keyElement() != null);
                        field.Annotations.AddRange(elemAnnotations);
                        aspect.Fields.Add(field);
                    }
                }
                else if (elem.associationDef() != null)
                {
                    var assoc = _elemBuilder.VisitAssociationDef(elem.associationDef());
                    assoc.Annotations.AddRange(elemAnnotations);
                    aspect.Associations.Add(assoc);
                }
                else if (elem.compositionDef() != null)
                {
                    var comp = _elemBuilder.VisitCompositionDef(elem.compositionDef());
                    comp.Annotations.AddRange(elemAnnotations);
                    aspect.Compositions.Add(comp);
                }
                else if (elem.actionDef() != null)
                {
                    try
                    {
                        var action = VisitActionDef(elem.actionDef());
                        action.Annotations.AddRange(elemAnnotations);
                        aspect.Actions.Add(action);
                    }
                    catch (Exception ex)
                    {
                        AddParseWarning(elem.Start.Line, "AspectAction", $"Failed to parse action in aspect: {ex.Message}");
                    }
                }
                else if (elem.functionDef() != null)
                {
                    try
                    {
                        var function = VisitFunctionDef(elem.functionDef());
                        function.Annotations.AddRange(elemAnnotations);
                        aspect.Functions.Add(function);
                    }
                    catch (Exception ex)
                    {
                        AddParseWarning(elem.Start.Line, "AspectFunction", $"Failed to parse function in aspect: {ex.Message}");
                    }
                }
                else if (elem.indexDef() != null)
                {
                    var idx = _elemBuilder.VisitIndexDef(elem.indexDef());
                    aspect.Indexes.Add(idx);
                }
                else if (elem.constraintDef() != null)
                {
                    var constraint = _elemBuilder.VisitConstraintDef(elem.constraintDef());
                    aspect.Constraints.Add(constraint);
                }
            }
        }

        aspect.Annotations.AddRange(annotations);
        return aspect;
    }

    public BmExtension VisitExtendDef(BmmdlParser.ExtendDefContext context, List<BmAnnotation> annotations)
    {
        var ext = new BmExtension
        {
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        // Target kind
        if (context.ENTITY() != null) ext.TargetKind = "entity";
        else if (context.TYPE() != null) ext.TargetKind = "type";
        else if (context.ASPECT() != null) ext.TargetKind = "aspect";
        else if (context.SERVICE() != null) ext.TargetKind = "service";
        else if (context.ENUM() != null) ext.TargetKind = "enum";

        // Target name (first identifierReference)
        var refs = context.identifierReference();
        if (refs.Length > 0)
        {
            ext.TargetName = refs[0].GetText();
            ext.Name = ext.TargetName;
        }

        // WITH aspects (remaining identifierReferences after the target, if WITH keyword present)
        if (context.WITH() != null && refs.Length > 1)
        {
            for (int i = 1; i < refs.Length; i++)
            {
                ext.WithAspects.Add(refs[i].GetText());
            }
        }

        // Inline elements
        foreach (var elem in context.entityElement())
        {
            var elemAnnotations = elem.annotation().Select(_elemBuilder.VisitAnnotation).ToList();
            
            if (elem.fieldDef() != null || elem.keyElement() != null)
            {
                var fieldCtx = elem.fieldDef() ?? elem.keyElement()?.fieldDef();
                if (fieldCtx != null)
                {
                    var field = _elemBuilder.VisitFieldDef(fieldCtx, elem.keyElement() != null);
                    field.Annotations.AddRange(elemAnnotations);
                    ext.Fields.Add(field);
                }
            }
            else if (elem.associationDef() != null)
            {
                var assoc = _elemBuilder.VisitAssociationDef(elem.associationDef());
                assoc.Annotations.AddRange(elemAnnotations);
                ext.Associations.Add(assoc);
            }
            else if (elem.compositionDef() != null)
            {
                var comp = _elemBuilder.VisitCompositionDef(elem.compositionDef());
                comp.Annotations.AddRange(elemAnnotations);
                ext.Compositions.Add(comp);
            }
            else if (elem.actionDef() != null)
            {
                try
                {
                    var action = VisitActionDef(elem.actionDef());
                    action.Annotations.AddRange(elemAnnotations);
                    ext.Actions.Add(action);
                }
                catch (Exception ex)
                {
                    AddParseWarning(elem.Start.Line, "ExtendAction", $"Failed to parse action in extension: {ex.Message}");
                }
            }
            else if (elem.functionDef() != null)
            {
                try
                {
                    var function = VisitFunctionDef(elem.functionDef());
                    function.Annotations.AddRange(elemAnnotations);
                    ext.Functions.Add(function);
                }
                catch (Exception ex)
                {
                    AddParseWarning(elem.Start.Line, "ExtendFunction", $"Failed to parse function in extension: {ex.Message}");
                }
            }
            else if (elem.indexDef() != null)
            {
                var idx = _elemBuilder.VisitIndexDef(elem.indexDef());
                ext.Indexes.Add(idx);
            }
            else if (elem.constraintDef() != null)
            {
                var constraint = _elemBuilder.VisitConstraintDef(elem.constraintDef());
                ext.Constraints.Add(constraint);
            }
        }

        // Service entity exposures (entity X as Y; — only meaningful for extend service)
        foreach (var exposure in context.entityExposure())
        {
            var entity = new BmEntity
            {
                Name = exposure.IDENTIFIER().GetText(),
                SourceFile = _sourceFile,
                StartLine = exposure.Start.Line,
                EndLine = exposure.Stop.Line
            };
            entity.Aspects.Add(exposure.identifierReference().GetText()); // Source entity

            var projection = exposure.projectionClause();
            if (projection != null)
            {
                if (projection.STAR() != null)
                {
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
                    entity.IncludeFields = projection.projectionItem()
                        .Select(item => item.IDENTIFIER().Length > 0
                            ? item.IDENTIFIER(0).GetText()
                            : item.identifierReference()?.GetText() ?? "")
                        .Where(name => !string.IsNullOrEmpty(name))
                        .ToList();
                }
            }

            ext.ServiceEntities.Add(entity);
        }

        // Enum values (for extend enum)
        foreach (var val in context.enumValue())
        {
            var enumVal = new BmEnumValue
            {
                Name = val.IDENTIFIER().GetText(),
                Value = val.literal() != null ? _elemBuilder.ExtractLiteral(val.literal()) : null,
                SourceFile = _sourceFile,
                StartLine = val.Start.Line,
                EndLine = val.Stop.Line
            };
            enumVal.Annotations.AddRange(val.annotation().Select(_elemBuilder.VisitAnnotation));
            ext.EnumValues.Add(enumVal);
        }

        ext.Annotations.AddRange(annotations);
        return ext;
    }

    public BmModification VisitModifyDef(BmmdlParser.ModifyDefContext context)
    {
        var mod = new BmModification
        {
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        // Target kind
        if (context.ENTITY() != null) mod.TargetKind = "entity";
        else if (context.TYPE() != null) mod.TargetKind = "type";
        else if (context.ASPECT() != null) mod.TargetKind = "aspect";
        else if (context.SERVICE() != null) mod.TargetKind = "service";
        else if (context.ENUM() != null) mod.TargetKind = "enum";

        // Target name
        var targetRef = context.identifierReference();
        if (targetRef != null)
        {
            mod.TargetName = targetRef.GetText();
            mod.Name = mod.TargetName;
        }

        // Actions
        foreach (var actionCtx in context.modifyAction())
        {
            var action = VisitModifyAction(actionCtx);
            if (action != null)
                mod.Actions.Add(action);
        }

        return mod;
    }

    public List<BmAnnotateDirective> VisitAnnotateDef(BmmdlParser.AnnotateDefContext context,
        string currentNamespace)
    {
        var targetName = context.identifierReference().GetText();
        var directives = new List<BmAnnotateDirective>();

        foreach (var item in context.annotateItem())
        {
            string? targetField = null;
            if (item.IDENTIFIER() != null)
            {
                targetField = item.IDENTIFIER().GetText();
            }

            var annotation = _elemBuilder.VisitAnnotation(item.annotation());

            var directive = new BmAnnotateDirective
            {
                TargetName = targetName,
                TargetField = targetField,
                Namespace = currentNamespace,
                SourceFile = _sourceFile,
                Line = item.Start.Line
            };
            directive.Annotations.Add(annotation);
            directives.Add(directive);
        }

        return directives;
    }

    public BmModifyAction? VisitModifyAction(BmmdlParser.ModifyActionContext context)
    {
        var identifiers = context.IDENTIFIER();
        
        if (context.REMOVE() != null && identifiers.Length > 0)
        {
            return new BmRemoveFieldAction
            {
                FieldName = identifiers[0].GetText(),
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };
        }
        else if (context.RENAME() != null && identifiers.Length >= 2)
        {
            return new BmRenameFieldAction
            {
                OldName = identifiers[0].GetText(),
                NewName = identifiers[1].GetText(),
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };
        }
        else if (context.CHANGE() != null && context.TYPE() != null && identifiers.Length > 0)
        {
            return new BmChangeTypeAction
            {
                FieldName = identifiers[0].GetText(),
                NewTypeString = context.typeReference()?.GetText() ?? "",
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };
        }
        else if (context.ADD() != null && context.fieldDef() != null)
        {
            var field = _elemBuilder.VisitFieldDef(context.fieldDef(), false);
            return new BmAddFieldAction
            {
                Field = field,
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };
        }
        else if (context.ADD() != null && context.fieldDef() == null && identifiers.Length > 0)
        {
            // Enum member addition: add MemberName = value;
            return new BmAddEnumMemberAction
            {
                Member = new BmEnumValue
                {
                    Name = identifiers[0].GetText(),
                    Value = context.literal() != null ? _elemBuilder.ExtractLiteral(context.literal()) : null,
                    SourceFile = _sourceFile,
                    StartLine = context.Start.Line
                },
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };
        }
        else if (context.MODIFY() != null && identifiers.Length > 0 && context.modifyProp() != null)
        {
            var modAction = new BmModifyFieldAction
            {
                FieldName = identifiers[0].GetText(),
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };
            
            foreach (var propCtx in context.modifyProp())
            {
                if (propCtx.typeReference() != null)
                {
                    modAction.NewTypeString = propCtx.typeReference().GetText();
                }
                else if (propCtx.expression() != null)
                {
                    modAction.NewDefaultValueString = propCtx.expression().GetText();
                }
                else if (propCtx.annotation() != null)
                {
                    modAction.Annotations.Add(_elemBuilder.VisitAnnotation(propCtx.annotation()));
                }
            }
            
            return modAction;
        }

        return null;
    }

    // Action/Function parsing delegated to shared helper

    private BmAction VisitActionDef(BmmdlParser.ActionDefContext context)
        => ActionFunctionParsingHelper.ParseAction(context, _elemBuilder, _exprBuilder, _stmtBuilder, _sourceFile, AddParseWarning, AddParseWarning);

    private BmFunction VisitFunctionDef(BmmdlParser.FunctionDefContext context)
        => ActionFunctionParsingHelper.ParseFunction(context, _elemBuilder, _stmtBuilder, _sourceFile);

}
