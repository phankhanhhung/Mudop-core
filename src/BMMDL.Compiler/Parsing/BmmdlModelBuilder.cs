using Antlr4.Runtime.Misc;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Expressions;
using Microsoft.Extensions.Logging;
using BMMDL.Compiler.Services;

namespace BMMDL.Compiler.Parsing;

/// <summary>
/// Visitor that builds a BmModel from the ANTLR parse tree.
/// Delegates to focused builders for entity elements, entities, and services.
/// </summary>
public class BmmdlModelBuilder : BmmdlParserBaseVisitor<BmModel>
{
    private readonly string? _sourceFile;
    private string _currentNamespace = "";
    private BmModel _model = new();
    private readonly BmExpressionBuilder _exprBuilder;
    private readonly BmStatementBuilder _stmtBuilder;
    private readonly BmAccessControlBuilder _acBuilder;
    private readonly BmEntityElementBuilder _elemBuilder;
    private readonly BmEntityBuilder _entityBuilder;
    private readonly BmServiceBuilder _serviceBuilder;
    private readonly BmMigrationBuilder _migrationBuilder;
    private readonly BmSeedBuilder _seedBuilder;
    private readonly ILogger _logger;

    /// <summary>
    /// Parse warnings collected during model building.
    /// These are non-fatal issues where the string representation is preserved.
    /// </summary>
    public List<ParseDiagnostic> Diagnostics { get; } = new();

    public BmmdlModelBuilder(string? sourceFile = null)
    {
        _sourceFile = sourceFile;
        _exprBuilder = new BmExpressionBuilder(sourceFile);
        _stmtBuilder = new BmStatementBuilder(_exprBuilder, sourceFile, Diagnostics);
        _acBuilder = new BmAccessControlBuilder(_exprBuilder, sourceFile, Diagnostics);
        _elemBuilder = new BmEntityElementBuilder(sourceFile, Diagnostics, _exprBuilder);
        _entityBuilder = new BmEntityBuilder(sourceFile, Diagnostics, _elemBuilder, _exprBuilder, _stmtBuilder);
        _serviceBuilder = new BmServiceBuilder(sourceFile, Diagnostics, _elemBuilder, _exprBuilder, _stmtBuilder);
        _migrationBuilder = new BmMigrationBuilder(
            sourceFile, Diagnostics,
            _elemBuilder.VisitFieldDef, _elemBuilder.VisitAssociationDef, _elemBuilder.VisitAnnotation,
            _elemBuilder.VisitCompositionDef, _elemBuilder.VisitIndexDef, _elemBuilder.VisitConstraintDef);
        _seedBuilder = new BmSeedBuilder(sourceFile, Diagnostics, _exprBuilder);
        _logger = CompilerLoggerFactory.CreateLogger("ModelBuilder");
    }

    #region Diagnostics

    /// <summary>
    /// Add a parse warning. Delegates to ParseDiagnosticHelper.
    /// </summary>
    private void AddParseWarning(int line, string context, string message)
    {
        ParseDiagnosticHelper.AddParseWarning(Diagnostics, _logger, line, context, message, _sourceFile);
    }

    /// <summary>
    /// Add a parse warning with exception details. Delegates to ParseDiagnosticHelper.
    /// </summary>
    private void AddParseWarning(int line, string context, string message, Exception ex)
    {
        ParseDiagnosticHelper.AddParseWarning(Diagnostics, _logger, line, context, message, ex, _sourceFile);
    }

    #endregion

    #region Compilation Unit & Module

    public override BmModel VisitCompilationUnit([NotNull] BmmdlParser.CompilationUnitContext context)
    {
        _model = new BmModel();

        // Handle new nested syntax: moduleBlock
        if (context.moduleBlock() != null)
        {
            VisitModuleBlock(context.moduleBlock());
        }
        // Handle new nested syntax: namespaceBlock (standalone without module)
        else if (context.namespaceBlock() != null)
        {
            VisitNamespaceBlock(context.namespaceBlock());
        }
        // Handle legacy flat syntax
        else if (context.legacyCompilationUnit() != null)
        {
            VisitLegacyCompilationUnit(context.legacyCompilationUnit());
        }

        return _model;
    }

    private new void VisitModuleBlock(BmmdlParser.ModuleBlockContext context)
    {
        // Create module declaration
        var module = new BmModuleDeclaration
        {
            Name = context.IDENTIFIER().GetText(),
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        // Version
        if (context.moduleVersion() != null)
        {
            var versionText = context.moduleVersion().STRING_LITERAL().GetText();
            module.Version = versionText.Trim('\'', '"');
        }

        // Properties
        foreach (var prop in context.moduleProperty())
        {
            ProcessModuleProperty(prop, module);
        }

        _model.Module = module;

        // Process nested namespace blocks
        foreach (var nsBlock in context.namespaceBlock())
        {
            VisitNamespaceBlock(nsBlock);
        }
    }

    private new void VisitNamespaceBlock(BmmdlParser.NamespaceBlockContext context)
    {
        // Set namespace from block
        _currentNamespace = context.qualifiedName().GetText();
        if (_model.Namespace == null)
        {
            _model.Namespace = _currentNamespace;
        }

        // Process imports within namespace block
        foreach (var importStmt in context.importStmt())
        {
            VisitImportStmt(importStmt);
        }

        // Process definitions within namespace block
        foreach (var def in context.definition())
        {
            VisitDefinition(def);
        }
    }

    private new void VisitImportStmt(BmmdlParser.ImportStmtContext context)
    {
        var import = new BmImport();

        // Extract the namespace/symbol path
        import.Path = context.identifierReference().GetText();

        // Check for alias (e.g., "using alias: ns.path from 'source';")
        if (context.importAlias() != null)
        {
            import.Alias = context.importAlias().IDENTIFIER().GetText();
        }

        // Check for source file/module (e.g., "using ns from 'file.bmmdl';")
        if (context.STRING_LITERAL() != null)
        {
            import.Source = context.STRING_LITERAL().GetText().Trim('\'', '"');
        }

        _model.Imports.Add(import);

        // Also add the path to module imports for symbol resolution compatibility
        if (_model.Module != null && !_model.Module.Imports.Contains(import.Path))
        {
            _model.Module.Imports.Add(import.Path);
        }
    }

    private new void VisitLegacyCompilationUnit(BmmdlParser.LegacyCompilationUnitContext context)
    {
        // Module declaration (optional)
        if (context.moduleDecl() != null)
        {
            _model.Module = VisitModuleDecl(context.moduleDecl());
        }

        // Namespace
        if (context.namespaceStmt() != null)
        {
            _currentNamespace = context.namespaceStmt().qualifiedName().GetText();
            _model.Namespace = _currentNamespace;
        }

        // Process import statements
        foreach (var importStmt in context.importStmt())
        {
            VisitImportStmt(importStmt);
        }

        // Process definitions
        foreach (var def in context.definition())
        {
            VisitDefinition(def);
        }
    }

    private void ProcessModuleProperty(BmmdlParser.ModulePropertyContext prop, BmModuleDeclaration module)
    {
        if (prop.AUTHOR() != null)
        {
            module.Author = prop.STRING_LITERAL().GetText().Trim('\'', '"');
        }
        else if (prop.DESCRIPTION() != null)
        {
            module.Description = prop.STRING_LITERAL().GetText().Trim('\'', '"');
        }
        else if (prop.DEPENDS() != null)
        {
            var dep = new BmModuleDependency
            {
                ModuleName = prop.IDENTIFIER().GetText(),
                VersionRange = prop.moduleVersionRange()?.STRING_LITERAL().GetText().Trim('\'', '"') ?? ""
            };
            module.Dependencies.Add(dep);
        }
        else if (prop.PUBLISHES() != null)
        {
            foreach (var ns in prop.identifierReference())
            {
                module.Publishes.Add(ns.GetText());
            }
        }
        else if (prop.IMPORTS() != null)
        {
            foreach (var ns in prop.identifierReference())
            {
                module.Imports.Add(ns.GetText());
            }
        }
        else if (prop.TENANT() != null && prop.AWARE() != null)
        {
            // Parse tenant-aware: true/false (consistent with VisitModuleDecl)
            if (prop.TRUE() != null)
            {
                module.TenantAware = true;
            }
            else if (prop.FALSE() != null)
            {
                module.TenantAware = false;
            }
        }
    }

    private new BmModuleDeclaration VisitModuleDecl(BmmdlParser.ModuleDeclContext context)
    {
        var module = new BmModuleDeclaration
        {
            Name = context.IDENTIFIER().GetText(),
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        // Version
        if (context.moduleVersion() != null)
        {
            var versionText = context.moduleVersion().STRING_LITERAL().GetText();
            module.Version = versionText.Trim('\'', '"');
        }

        // Properties
        foreach (var prop in context.moduleProperty())
        {
            ProcessModuleProperty(prop, module);
        }

        return module;
    }

    #endregion

    #region Definition Dispatch

    public override BmModel VisitDefinition([NotNull] BmmdlParser.DefinitionContext context)
    {
        var annotations = context.annotation().Select(_elemBuilder.VisitAnnotation).ToList();

        if (context.entityDef() != null)
        {
            var entity = _entityBuilder.VisitEntityDef(context.entityDef(), annotations, _currentNamespace, _model);
            _model.Entities.Add(entity);
        }
        else if (context.typeDef() != null)
        {
            var type = _entityBuilder.VisitTypeDef(context.typeDef(), annotations, _currentNamespace);
            _model.Types.Add(type);
        }
        else if (context.enumDef() != null)
        {
            var enumDef = _entityBuilder.VisitEnumDef(context.enumDef(), annotations, _currentNamespace);
            _model.Enums.Add(enumDef);
        }
        else if (context.aspectDef() != null)
        {
            var aspect = _entityBuilder.VisitAspectDef(context.aspectDef(), annotations, _currentNamespace,
                _serviceBuilder.VisitRuleDef, _acBuilder.Build);
            _model.Aspects.Add(aspect);
        }
        else if (context.serviceDef() != null)
        {
            var service = _serviceBuilder.VisitServiceDef(context.serviceDef(), annotations, _currentNamespace);
            _model.Services.Add(service);
        }
        else if (context.tableDef() != null)
        {
            var view = _serviceBuilder.VisitTableDef(context.tableDef(), annotations, _currentNamespace);
            _model.Views.Add(view);
        }
        else if (context.accessControlDef() != null)
        {
            var acl = _acBuilder.Build(context.accessControlDef());
            _model.AccessControls.Add(acl);
        }
        else if (context.ruleDef() != null)
        {
            var rule = _serviceBuilder.VisitRuleDef(context.ruleDef(), annotations);
            _model.Rules.Add(rule);
        }
        else if (context.sequenceDef() != null)
        {
            var seq = _serviceBuilder.VisitSequenceDef(context.sequenceDef(), annotations);
            _model.Sequences.Add(seq);
        }
        else if (context.eventDef() != null)
        {
            var evt = _serviceBuilder.VisitEventDef(context.eventDef(), annotations, _model);
            _model.Events.Add(evt);
        }
        else if (context.extendDef() != null)
        {
            var ext = _entityBuilder.VisitExtendDef(context.extendDef(), annotations);
            _model.Extensions.Add(ext);
        }
        else if (context.modifyDef() != null)
        {
            var mod = _entityBuilder.VisitModifyDef(context.modifyDef());
            _model.Modifications.Add(mod);
        }
        else if (context.annotateDef() != null)
        {
            var directives = _entityBuilder.VisitAnnotateDef(context.annotateDef(), _currentNamespace);
            _model.AnnotateDirectives.AddRange(directives);
        }
        else if (context.migrationDef() != null)
        {
            var migration = _migrationBuilder.Build(context.migrationDef(), _currentNamespace, annotations);
            _model.Migrations.Add(migration);
        }
        else if (context.seedDef() != null)
        {
            var seed = _seedBuilder.Build(context.seedDef(), _currentNamespace, annotations);
            _model.Seeds.Add(seed);
        }
        else if (context.contextDef() != null)
        {
            // contextDef is recognized but not fully modeled (low-value feature).
            // Process nested definitions within the context block so they are not lost.
            var ctxDef = context.contextDef();
            foreach (var nestedDef in ctxDef.definition())
            {
                VisitDefinition(nestedDef);
            }
        }

        return _model;
    }

    #endregion
}
