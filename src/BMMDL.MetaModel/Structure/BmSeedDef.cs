using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Expressions;

namespace BMMDL.MetaModel.Structure;

/// <summary>
/// Seed data definition — declares initial data rows for an entity.
/// </summary>
public class BmSeedDef : INamedElement, IAnnotatable
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";

    /// <summary>Target entity name (qualified or simple).</summary>
    public string EntityName { get; set; } = "";

    /// <summary>Column names in declaration order.</summary>
    public List<string> Columns { get; } = new();

    /// <summary>Data rows. Each row has one expression per column.</summary>
    public List<BmSeedRow> Rows { get; } = new();

    public List<BmAnnotation> Annotations { get; } = new();

    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public BmAnnotation? GetAnnotation(string name) => Annotations.FirstOrDefault(a => a.Name == name);
    public bool HasAnnotation(string name) => Annotations.Any(a => a.Name == name);
}

/// <summary>
/// A single row of seed data values.
/// </summary>
public class BmSeedRow
{
    public List<BmExpression> Values { get; } = new();
    public int Line { get; set; }
}
