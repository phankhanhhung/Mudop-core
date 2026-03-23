using BMMDL.MetaModel;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;
using Microsoft.Extensions.Logging;
using BMMDL.Compiler.Services;

namespace BMMDL.Compiler.Parsing;

/// <summary>
/// Builds seed data definition AST nodes from ANTLR parse tree contexts.
/// Extracted from BmmdlModelBuilder to follow the builder delegation pattern.
/// </summary>
public class BmSeedBuilder
{
    private readonly string? _sourceFile;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly BmExpressionBuilder _exprBuilder;
    private static readonly Lazy<ILogger> _logger = new(() =>
        CompilerLoggerFactory.CreateLogger(nameof(BmSeedBuilder)));

    public BmSeedBuilder(
        string? sourceFile,
        List<ParseDiagnostic> diagnostics,
        BmExpressionBuilder exprBuilder)
    {
        _sourceFile = sourceFile;
        _diagnostics = diagnostics;
        _exprBuilder = exprBuilder;
    }

    /// <summary>
    /// Build a BmSeedDef from a seedDef context.
    /// </summary>
    public BmSeedDef Build(BmmdlParser.SeedDefContext context, string currentNamespace, List<BmAnnotation> annotations)
    {
        var seed = new BmSeedDef
        {
            Name = context.IDENTIFIER().GetText(),
            EntityName = context.identifierReference().GetText(),
            Namespace = currentNamespace,
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        // Parse body: INSERT (col1, col2, ...) VALUES (row1), (row2), ...
        var body = context.seedBody();

        // Parse column names from fieldRefList
        var fieldRefList = body.fieldRefList();
        foreach (var id in fieldRefList.IDENTIFIER())
        {
            seed.Columns.Add(id.GetText());
        }

        // Parse each seed row
        foreach (var rowCtx in body.seedRow())
        {
            var row = BuildSeedRow(rowCtx, seed.Columns.Count, seed.Name);
            seed.Rows.Add(row);
        }

        seed.Annotations.AddRange(annotations);
        return seed;
    }

    private BmSeedRow BuildSeedRow(BmmdlParser.SeedRowContext context, int expectedColumnCount, string seedName)
    {
        var row = new BmSeedRow
        {
            Line = context.Start.Line
        };

        var expressions = context.expression();
        foreach (var exprCtx in expressions)
        {
            try
            {
                var expr = _exprBuilder.Visit(exprCtx);
                row.Values.Add(expr);
            }
            catch (Exception ex)
            {
                // Preserve the raw text as a string literal fallback
                ParseDiagnosticHelper.AddParseWarning(
                    _diagnostics, _logger.Value, exprCtx.Start.Line,
                    "SeedRow", $"Failed to parse expression '{exprCtx.GetText()}' in seed '{seedName}'",
                    ex, _sourceFile);
                row.Values.Add(BmLiteralExpression.String(exprCtx.GetText()));
            }
        }

        // Emit parse-time diagnostic if row value count doesn't match column count
        if (row.Values.Count != expectedColumnCount)
        {
            ParseDiagnosticHelper.AddParseWarning(
                _diagnostics, _logger.Value, context.Start.Line,
                "SeedRow",
                $"Seed '{seedName}' row at line {context.Start.Line} has {row.Values.Count} values but {expectedColumnCount} columns declared",
                _sourceFile);
        }

        return row;
    }
}
