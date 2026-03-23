using BMMDL.MetaModel;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Structure;

namespace BMMDL.Compiler.Parsing;

/// <summary>
/// Builds migration definition AST nodes from ANTLR parse tree contexts.
/// Extracted from BmmdlModelBuilder to reduce class size.
/// </summary>
public class BmMigrationBuilder
{
    private readonly string? _sourceFile;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly Func<BmmdlParser.FieldDefContext, bool, BmField> _parseField;
    private readonly Func<BmmdlParser.AssociationDefContext, BmAssociation> _parseAssociation;
    private readonly Func<BmmdlParser.AnnotationContext, BmAnnotation> _parseAnnotation;
    private readonly Func<BmmdlParser.CompositionDefContext, BmComposition> _parseComposition;
    private readonly Func<BmmdlParser.IndexDefContext, BmIndex> _parseIndex;
    private readonly Func<BmmdlParser.ConstraintDefContext, BmConstraint> _parseConstraint;

    public BmMigrationBuilder(
        string? sourceFile,
        List<ParseDiagnostic> diagnostics,
        Func<BmmdlParser.FieldDefContext, bool, BmField> parseField,
        Func<BmmdlParser.AssociationDefContext, BmAssociation> parseAssociation,
        Func<BmmdlParser.AnnotationContext, BmAnnotation> parseAnnotation,
        Func<BmmdlParser.CompositionDefContext, BmComposition> parseComposition,
        Func<BmmdlParser.IndexDefContext, BmIndex> parseIndex,
        Func<BmmdlParser.ConstraintDefContext, BmConstraint> parseConstraint)
    {
        _sourceFile = sourceFile;
        _diagnostics = diagnostics;
        _parseField = parseField;
        _parseAssociation = parseAssociation;
        _parseAnnotation = parseAnnotation;
        _parseComposition = parseComposition;
        _parseIndex = parseIndex;
        _parseConstraint = parseConstraint;
    }

    /// <summary>
    /// Build a BmMigrationDef from a migrationDef context.
    /// </summary>
    public BmMigrationDef Build(BmmdlParser.MigrationDefContext context, string currentNamespace, List<BmAnnotation> annotations)
    {
        var migration = new BmMigrationDef
        {
            Name = context.STRING_LITERAL().GetText().Trim('\'', '"'),
            Namespace = currentNamespace,
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        // Parse properties (version, author, description, breaking, depends)
        foreach (var prop in context.migrationProperty())
        {
            var stringLiterals = prop.STRING_LITERAL();
            if (prop.VERSION() != null && stringLiterals.Length > 0)
            {
                migration.Version = stringLiterals[0].GetText().Trim('\'', '"');
            }
            else if (prop.AUTHOR() != null && stringLiterals.Length > 0)
            {
                migration.Author = stringLiterals[0].GetText().Trim('\'', '"');
            }
            else if (prop.DESCRIPTION() != null && stringLiterals.Length > 0)
            {
                migration.Description = stringLiterals[0].GetText().Trim('\'', '"');
            }
            else if (prop.BREAKING() != null)
            {
                migration.Breaking = prop.TRUE() != null;
            }
            else if (prop.DEPENDS() != null)
            {
                foreach (var dep in stringLiterals)
                {
                    migration.Dependencies.Add(dep.GetText().Trim('\'', '"'));
                }
            }
        }

        // Parse body (up/down blocks)
        var body = context.migrationBody();
        if (body.upBlock() != null)
        {
            foreach (var step in body.upBlock().migrationStep())
            {
                var migrationStep = BuildMigrationStep(step);
                if (migrationStep != null)
                    migration.UpSteps.Add(migrationStep);
            }
        }

        if (body.downBlock() != null)
        {
            foreach (var step in body.downBlock().migrationStep())
            {
                var migrationStep = BuildMigrationStep(step);
                if (migrationStep != null)
                    migration.DownSteps.Add(migrationStep);
            }
        }

        migration.Annotations.AddRange(annotations);
        return migration;
    }

    private BmMigrationStep? BuildMigrationStep(BmmdlParser.MigrationStepContext context)
    {
        if (context.alterEntityStep() != null)
        {
            return BuildAlterEntityStep(context.alterEntityStep());
        }
        else if (context.addEntityStep() != null)
        {
            return BuildAddEntityStep(context.addEntityStep());
        }
        else if (context.dropEntityStep() != null)
        {
            return BuildDropEntityStep(context.dropEntityStep());
        }
        else if (context.transformStep() != null)
        {
            return BuildTransformStep(context.transformStep());
        }

        return null;
    }

    private BmAlterEntityStep BuildAlterEntityStep(BmmdlParser.AlterEntityStepContext context)
    {
        var step = new BmAlterEntityStep
        {
            EntityName = context.identifierReference().GetText(),
            SourceFile = _sourceFile,
            StartLine = context.Start.Line
        };

        foreach (var actionCtx in context.alterAction())
        {
            var action = BuildAlterAction(actionCtx);
            if (action != null)
                step.Actions.Add(action);
        }

        return step;
    }

    private BmAlterAction? BuildAlterAction(BmmdlParser.AlterActionContext context)
    {
        // ADD fieldDef ;
        if (context.fieldDef() != null)
        {
            var field = _parseField(context.fieldDef(), false);
            return new BmAlterAddColumnAction
            {
                FieldName = field.Name,
                TypeString = field.TypeString,
                IsKey = field.IsKey,
                IsNullable = field.IsNullable,
                DefaultValue = field.DefaultValueString,
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };
        }

        var identifiers = context.IDENTIFIER();

        // DROP COLUMN identifier ;
        if (context.DROP() != null && context.COLUMN() != null && identifiers.Length > 0)
        {
            return new BmAlterDropColumnAction
            {
                ColumnName = identifiers[0].GetText(),
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };
        }

        // RENAME COLUMN identifier TO identifier ;
        if (context.RENAME() != null && context.COLUMN() != null && identifiers.Length >= 2)
        {
            return new BmAlterRenameColumnAction
            {
                OldName = identifiers[0].GetText(),
                NewName = identifiers[1].GetText(),
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };
        }

        // ALTER COLUMN identifier alterColumnAction* ;
        if (context.ALTER() != null && context.COLUMN() != null && identifiers.Length > 0)
        {
            var alterCol = new BmAlterColumnAction
            {
                ColumnName = identifiers[0].GetText(),
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };

            foreach (var colAction in context.alterColumnAction())
            {
                var change = BuildAlterColumnAction(colAction);
                if (change != null)
                    alterCol.Changes.Add(change);
            }

            return alterCol;
        }

        // ADD indexDef ;
        if (context.indexDef() != null)
        {
            var indexCtx = context.indexDef();
            var action = new BmAlterAddIndexAction
            {
                IndexName = indexCtx.IDENTIFIER().GetText(),
                IsUnique = indexCtx.UNIQUE() != null,
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };

            foreach (var field in indexCtx.fieldRefList().IDENTIFIER())
            {
                action.Columns.Add(field.GetText());
            }

            return action;
        }

        // DROP INDEX identifier ;
        if (context.DROP() != null && context.INDEX() != null && identifiers.Length > 0)
        {
            return new BmAlterDropIndexAction
            {
                IndexName = identifiers[0].GetText(),
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };
        }

        // ADD constraintDef ;
        if (context.constraintDef() != null)
        {
            var constraintCtx = context.constraintDef();
            return new BmAlterAddConstraintAction
            {
                ConstraintName = constraintCtx.IDENTIFIER().GetText(),
                ConstraintText = constraintCtx.constraintType().GetText(),
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };
        }

        // DROP CONSTRAINT identifier ;
        if (context.DROP() != null && context.CONSTRAINT() != null && identifiers.Length > 0)
        {
            return new BmAlterDropConstraintAction
            {
                ConstraintName = identifiers[0].GetText(),
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };
        }

        return null;
    }

    private static BmAlterColumnChange? BuildAlterColumnAction(BmmdlParser.AlterColumnActionContext context)
    {
        // TYPE typeReference
        if (context.TYPE() != null && context.typeReference() != null)
        {
            return new BmChangeTypeChange
            {
                NewTypeString = context.typeReference().GetText()
            };
        }

        // SET DEFAULT expression
        if (context.SET() != null && context.DEFAULT() != null && context.expression() != null)
        {
            return new BmSetDefaultChange
            {
                Expression = context.expression().GetText()
            };
        }

        // DROP DEFAULT
        if (context.DROP() != null && context.DEFAULT() != null)
        {
            return new BmDropDefaultChange();
        }

        // SET NULLABLE / SET NOT NULLABLE
        if (context.SET() != null && context.NULLABLE() != null)
        {
            return new BmSetNullableChange
            {
                IsNullable = context.NOT() == null
            };
        }

        return null;
    }

    private BmAddEntityStep BuildAddEntityStep(BmmdlParser.AddEntityStepContext context)
    {
        var step = new BmAddEntityStep
        {
            EntityName = context.identifierReference().GetText(),
            ElementsText = context.entityElement().Length > 0
                ? string.Join("\n", context.entityElement().Select(e => e.GetText()))
                : "",
            SourceFile = _sourceFile,
            StartLine = context.Start.Line
        };

        // Parse entity elements into structured fields, associations, compositions, indexes, constraints
        foreach (var elem in context.entityElement())
        {
            var elemAnnotations = elem.annotation().Select(a => _parseAnnotation(a)).ToList();

            if (elem.fieldDef() != null || elem.keyElement() != null)
            {
                var fieldCtx = elem.fieldDef() ?? elem.keyElement()?.fieldDef();
                if (fieldCtx != null)
                {
                    var field = _parseField(fieldCtx, elem.keyElement() != null);
                    field.Annotations.AddRange(elemAnnotations);
                    step.Fields.Add(field);
                }
            }
            else if (elem.associationDef() != null)
            {
                var assoc = _parseAssociation(elem.associationDef());
                assoc.Annotations.AddRange(elemAnnotations);
                step.Associations.Add(assoc);
            }
            else if (elem.compositionDef() != null)
            {
                var comp = _parseComposition(elem.compositionDef());
                comp.Annotations.AddRange(elemAnnotations);
                step.Compositions.Add(comp);
            }
            else if (elem.indexDef() != null)
            {
                var idx = _parseIndex(elem.indexDef());
                step.Indexes.Add(idx);
            }
            else if (elem.constraintDef() != null)
            {
                var constraint = _parseConstraint(elem.constraintDef());
                step.Constraints.Add(constraint);
            }
        }

        return step;
    }

    private BmDropEntityStep BuildDropEntityStep(BmmdlParser.DropEntityStepContext context)
    {
        return new BmDropEntityStep
        {
            EntityName = context.identifierReference().GetText(),
            SourceFile = _sourceFile,
            StartLine = context.Start.Line
        };
    }

    private BmTransformStep BuildTransformStep(BmmdlParser.TransformStepContext context)
    {
        var step = new BmTransformStep
        {
            EntityName = context.identifierReference().GetText(),
            SourceFile = _sourceFile,
            StartLine = context.Start.Line
        };

        foreach (var actionCtx in context.transformAction())
        {
            var action = BuildTransformAction(actionCtx);
            if (action != null)
                step.Actions.Add(action);
        }

        return step;
    }

    private BmTransformAction? BuildTransformAction(BmmdlParser.TransformActionContext context)
    {
        // SET identifier = expression ;  (simple unconditional SET)
        if (context.SET() != null && context.UPDATE() == null && context.IDENTIFIER() != null)
        {
            return new BmTransformSetAction
            {
                FieldName = context.IDENTIFIER().GetText(),
                Expression = context.expression().GetText(),
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };
        }

        // UPDATE SET assignment [, assignment]* [WHERE condition] ;
        if (context.UPDATE() != null && context.transformAssignment() != null && context.transformAssignment().Length > 0)
        {
            var action = new BmTransformUpdateAction
            {
                WhereClause = context.whereClause()?.expression()?.GetText() ?? "",
                SourceFile = _sourceFile,
                StartLine = context.Start.Line
            };

            foreach (var assignCtx in context.transformAssignment())
            {
                action.Assignments.Add(new BmTransformAssignment
                {
                    FieldName = assignCtx.IDENTIFIER().GetText(),
                    Expression = assignCtx.expression().GetText()
                });
            }

            return action;
        }

        return null;
    }
}
