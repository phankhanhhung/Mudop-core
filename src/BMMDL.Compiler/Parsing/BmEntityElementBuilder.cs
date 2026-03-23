using BMMDL.MetaModel;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Expressions;

namespace BMMDL.Compiler.Parsing;

/// <summary>
/// Builds individual entity elements (fields, associations, compositions, indexes, constraints)
/// and annotation/literal parsing. Shared by BmmdlModelBuilder, BmEntityBuilder, and BmMigrationBuilder.
/// </summary>
public class BmEntityElementBuilder : BuilderBase
{
    private readonly BmTypeReferenceBuilder _typeBuilder = new();
    private readonly BmExpressionBuilder _exprBuilder;

    public BmEntityElementBuilder(
        string? sourceFile,
        List<ParseDiagnostic> diagnostics,
        BmExpressionBuilder exprBuilder)
        : base(sourceFile, diagnostics, "EntityElementBuilder")
    {
        _exprBuilder = exprBuilder;
    }

    public (int min, int max, BmCardinality cardinality) ParseCardinality(
        BmmdlParser.CardinalityContext? context, 
        BmCardinality defaultCardinality = BmCardinality.ManyToOne)
    {
        // No cardinality specified - use defaults
        if (context == null)
        {
            return defaultCardinality switch
            {
                BmCardinality.OneToMany => (0, -1, BmCardinality.OneToMany),
                BmCardinality.OneToOne => (1, 1, BmCardinality.OneToOne),
                _ => (0, 1, BmCardinality.ManyToOne)  // Default: optional FK
            };
        }
        
        // Parse tokens: could be [*], [1], [0,1], [1,*], [*,*], etc.
        var tokens = context.children?.Select(c => c.GetText())
            .Where(t => t != "[" && t != "]" && t != ",")
            .ToList() ?? new List<string>();
        
        int min = 0;
        int max = 1;
        bool isTwoTokenNotation = tokens.Count == 2;
        
        if (tokens.Count == 0)
        {
            // Empty brackets [] means optional many: [0..*]
            min = 0;
            max = -1;
        }
        else if (tokens.Count == 1)
        {
            // Single value: [*] = [0..*], [1] = [1..1]
            var value = tokens[0];
            if (value == "*")
            {
                min = 0;
                max = -1;
            }
            else if (int.TryParse(value, out var n))
            {
                min = n;
                max = n;
            }
        }
        else if (tokens.Count == 2)
        {
            // Two values: [min, max]
            var minStr = tokens[0];
            var maxStr = tokens[1];
            
            min = minStr == "*" ? 0 : int.TryParse(minStr, out var n1) ? n1 : 0;
            max = maxStr == "*" ? -1 : int.TryParse(maxStr, out var n2) ? n2 : 1;
        }
        
        // Determine cardinality type based on min/max and notation
        BmCardinality cardinality;
        if (max == 1 && min == 1)
            cardinality = BmCardinality.OneToOne;
        else if (max == 1)
            cardinality = BmCardinality.ManyToOne;  // Optional FK
        else if (isTwoTokenNotation && tokens[0] == "*" && tokens[1] == "*")
            cardinality = BmCardinality.ManyToMany;  // [*,*] = many-to-many junction table
        else if (max == -1)
            cardinality = BmCardinality.OneToMany;
        else
            cardinality = BmCardinality.ManyToMany;
        
        return (min, max, cardinality);
    }

    public BmAnnotation VisitAnnotation(BmmdlParser.AnnotationContext context)
    {
        var name = context.identifierReference().GetText();
        object? value = null;
        var properties = new Dictionary<string, object?>();

        // Phase 8: Handle @Annotation { key: value } syntax (direct braces in annotation rule)
        if (context.LBRACE() != null && context.annotationProperty().Length > 0)
        {
            foreach (var propCtx in context.annotationProperty())
            {
                var propName = propCtx.softIdentifier().GetText();
                var propValue = ExtractAnnotationValue(propCtx.annotationValue());
                properties[propName] = propValue;
            }
        }
        // Handle @Annotation(value) or @Annotation: value syntax
        else if (context.annotationValue() != null)
        {
            var valueCtx = context.annotationValue();
            
            // Handle annotation properties inside annotationValue: @Annotation({ key: value })
            if (valueCtx.LBRACE() != null && valueCtx.annotationProperty().Length > 0)
            {
                foreach (var propCtx in valueCtx.annotationProperty())
                {
                    var propName = propCtx.softIdentifier().GetText();
                    var propValue = ExtractAnnotationValue(propCtx.annotationValue());
                    properties[propName] = propValue;
                }
            }
            else
            {
                value = ExtractAnnotationValue(valueCtx);
            }
        }

        return new BmAnnotation(name, value, properties)
        {
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };
    }

    public object? ExtractAnnotationValue(BmmdlParser.AnnotationValueContext context)
    {
        if (context.literal() != null)
        {
            return ExtractLiteral(context.literal());
        }
        
        // Handle arrays [value1, value2, ...]
        if (context.LBRACKET() != null)
        {
            var items = new List<object?>();
            foreach (var subValue in context.annotationValue())
            {
                items.Add(ExtractAnnotationValue(subValue));
            }
            return items;
        }
        
        // Handle nested objects { key: value, ... }
        if (context.LBRACE() != null)
        {
            var obj = new Dictionary<string, object?>();
            foreach (var propCtx in context.annotationProperty())
            {
                var propName = propCtx.softIdentifier().GetText();
                var propValue = ExtractAnnotationValue(propCtx.annotationValue());
                obj[propName] = propValue;
            }
            return obj;
        }
        
        // Handle enum-like values #Identifier
        if (context.HASH() != null)
        {
            return "#" + context.IDENTIFIER().GetText();
        }
        
        // Fall back to expression text
        if (context.expression() != null)
        {
            return context.expression().GetText();
        }
        
        // Fallback to raw text
        return context.GetText();
    }

    public object? ExtractLiteral(BmmdlParser.LiteralContext context)
    {
        if (context.STRING_LITERAL() != null)
        {
            var text = context.STRING_LITERAL().GetText();
            return text.Substring(1, text.Length - 2); // Remove quotes
        }
        if (context.INTEGER_LITERAL() != null)
        {
            var text = context.INTEGER_LITERAL().GetText();
            if (!int.TryParse(text, out var intVal))
            {
                AddParseWarning(context.Start.Line, "LiteralParse", $"Failed to parse integer literal '{text}', defaulting to 0");
                intVal = 0;
            }
            return intVal;
        }
        if (context.DECIMAL_LITERAL() != null)
        {
            var text = context.DECIMAL_LITERAL().GetText();
            if (!decimal.TryParse(text,
                System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                System.Globalization.CultureInfo.InvariantCulture, out var decVal))
            {
                AddParseWarning(context.Start.Line, "LiteralParse", $"Failed to parse decimal literal '{text}', defaulting to 0");
                decVal = 0m;
            }
            return decVal;
        }
        if (context.TRUE() != null) return true;
        if (context.FALSE() != null) return false;
        if (context.NULL() != null) return null;
        if (context.HASH() != null)
        {
            return "#" + context.IDENTIFIER().GetText();
        }
        return null;
    }

    public BmField VisitFieldDef(BmmdlParser.FieldDefContext context, bool isKey)
    {
        var typeText = context.typeReference().GetText();
        var defaultExprText = context.defaultExpr()?.expression().GetText();
        
        // Parse type reference
        BmTypeReference? typeRef = null;
        try { typeRef = _typeBuilder.Parse(typeText); }
        catch (Exception ex) { AddParseWarning(context.Start.Line, "TypeParse", $"Failed to parse type '{typeText}'", ex); }
        
        // Parse default expression
        BmExpression? defaultExpr = null;
        if (context.defaultExpr()?.expression() != null)
        {
            try { defaultExpr = _exprBuilder.Visit(context.defaultExpr().expression()); }
            catch (Exception ex) { AddParseWarning(context.Start.Line, "DefaultExpr", "Failed to parse default expression", ex); }
        }
        
        // Phase 7: Parse field modifiers
        bool isVirtual = false, isReadonly = false, isImmutable = false;
        foreach (var mod in context.fieldModifier())
        {
            if (mod.VIRTUAL() != null) isVirtual = true;
            if (mod.READONLY() != null) isReadonly = true;
            if (mod.IMMUTABLE() != null) isImmutable = true;
        }
        
        // Phase 7: Parse computed expression
        bool isComputed = false, isStored = false;
        string? computedExprString = null;
        BmExpression? computedExpr = null;
        if (context.computedExpr() != null)
        {
            var compCtx = context.computedExpr();
            isComputed = compCtx.COMPUTED() != null;
            isStored = compCtx.STORED() != null;
            computedExprString = compCtx.expression()?.GetText();
            if (compCtx.expression() != null)
            {
                try { computedExpr = _exprBuilder.Visit(compCtx.expression()); }
                catch (Exception ex) { AddParseWarning(context.Start.Line, "ComputedExpr", "Failed to parse computed expression", ex); }
            }
        }
        
        return new BmField
        {
            Name = context.softIdentifier().GetText(),
            TypeString = typeText,
            TypeRef = typeRef,
            IsKey = isKey,
            DefaultValueString = defaultExprText,
            DefaultExpr = defaultExpr,
            // Phase 7 properties
            IsVirtual = isVirtual,
            IsReadonly = isReadonly,
            IsImmutable = isImmutable,
            IsComputed = isComputed,
            IsStored = isStored,
            ComputedExprString = computedExprString,
            ComputedExpr = computedExpr,
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };
    }

    public BmAssociation VisitAssociationDef(BmmdlParser.AssociationDefContext context)
    {
        var name = context.IDENTIFIER()?.GetText() ?? "";
        var target = context.identifierReference().GetText();
        var conditionText = context.expression()?.GetText();
        
        // Parse on condition expression
        BmExpression? conditionExpr = null;
        if (context.expression() != null)
        {
            try { conditionExpr = _exprBuilder.Visit(context.expression()); }
            catch (Exception ex) { AddParseWarning(context.Start.Line, "AssocCondition", "Failed to parse association condition", ex); }
        }
        
        // Parse cardinality [min, max] or [min..max]
        var (minCard, maxCard, cardinality) = ParseCardinality(context.cardinality());
        
        return new BmAssociation
        {
            Name = name,
            TargetEntity = target,
            OnConditionString = conditionText,
            OnConditionExpr = conditionExpr,
            Cardinality = cardinality,
            MinCardinality = minCard,
            MaxCardinality = maxCard,
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };
    }

    public BmComposition VisitCompositionDef(BmmdlParser.CompositionDefContext context)
    {
        var name = context.IDENTIFIER()?.GetText() ?? "";
        var target = context.identifierReference().GetText();
        
        // Parse cardinality - compositions default to OneToMany
        var (minCard, maxCard, cardinality) = ParseCardinality(context.cardinality(), defaultCardinality: BmCardinality.OneToMany);
        
        return new BmComposition
        {
            Name = name,
            TargetEntity = target,
            Cardinality = cardinality,
            MinCardinality = minCard,
            MaxCardinality = maxCard,
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };
    }

    public BmIndex VisitIndexDef(BmmdlParser.IndexDefContext context)
    {
        var idx = new BmIndex
        {
            Name = context.IDENTIFIER().GetText(),
            IsUnique = context.UNIQUE() != null
        };
        
        foreach (var field in context.fieldRefList().IDENTIFIER())
        {
            idx.Fields.Add(field.GetText());
        }
        
        return idx;
    }

    public BmConstraint VisitConstraintDef(BmmdlParser.ConstraintDefContext context)
    {
        var name = context.IDENTIFIER().GetText();
        var constraintType = context.constraintType();
        
        if (constraintType.checkConstraint() != null)
        {
            var check = constraintType.checkConstraint();
            var conditionText = check.expression()?.GetText() ?? "";
            BmExpression? conditionExpr = null;
            if (check.expression() != null)
            {
                try { conditionExpr = _exprBuilder.Visit(check.expression()); }
                catch (Exception ex) { AddParseWarning(check.Start.Line, "CheckConstraint", "Failed to parse check constraint", ex); }
            }
            return new BmCheckConstraint
            {
                Name = name,
                ConditionString = conditionText,
                Condition = conditionExpr
            };
        }
        else if (constraintType.uniqueConstraint() != null)
        {
            var unique = constraintType.uniqueConstraint();
            var constraint = new BmUniqueConstraint { Name = name };
            foreach (var field in unique.fieldRefList().IDENTIFIER())
            {
                constraint.Fields.Add(field.GetText());
            }
            return constraint;
        }
        else if (constraintType.foreignKeyConstraint() != null)
        {
            var fk = constraintType.foreignKeyConstraint();
            var constraint = new BmForeignKeyConstraint
            {
                Name = name,
                ReferencedEntity = fk.identifierReference().GetText()
            };
            
            // Source fields
            foreach (var field in fk.fieldRefList(0).IDENTIFIER())
            {
                constraint.Fields.Add(field.GetText());
            }
            // Referenced fields
            foreach (var field in fk.fieldRefList(1).IDENTIFIER())
            {
                constraint.ReferencedFields.Add(field.GetText());
            }
            return constraint;
        }
        
        return new BmCheckConstraint { Name = name, ConditionString = "" };
    }

    public BmParameter ParseParameter(BmmdlParser.ParameterContext param)
    {
        var p = new BmParameter
        {
            Name = param.IDENTIFIER().GetText(),
            Type = param.typeReference().GetText(),
            SourceFile = _sourceFile,
            StartLine = param.Start.Line,
            EndLine = param.Stop.Line
        };
        if (param.expression() != null)
        {
            p.DefaultValueString = param.expression().GetText();
            try { p.DefaultValueAst = _exprBuilder.Visit(param.expression()); }
            catch (Exception ex) { AddParseWarning(param.Start.Line, "ParamDefault", "Failed to parse parameter default", ex); }
        }
        foreach (var annCtx in param.annotation())
        {
            p.Annotations.Add(VisitAnnotation(annCtx));
        }
        return p;
    }
}
