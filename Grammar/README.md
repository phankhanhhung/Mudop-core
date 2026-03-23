# BMMDL Grammar - ANTLR4

Business Meta Model Definition Language - ANTLR4 Grammar files for generating C# parser.

## Files

| File | Description |
|------|-------------|
| `BmmdlLexer.g4` | Lexer grammar with all tokens |
| `BmmdlParser.g4` | Parser grammar with all rules |

## Prerequisites

1. **Java Runtime** (JRE 11+)
2. **ANTLR4 Tool** - Download from [antlr.org](https://www.antlr.org/download.html)

## Generate C# Code

### Option 1: Command Line

```bash
# Download ANTLR4 jar
curl -O https://www.antlr.org/download/antlr-4.13.1-complete.jar

# Generate C# code
java -jar antlr-4.13.1-complete.jar -Dlanguage=CSharp -visitor -no-listener BmmdlLexer.g4 BmmdlParser.g4

# Output files:
# - BmmdlLexer.cs
# - BmmdlParser.cs
# - BmmdlParserVisitor.cs
# - BmmdlParserBaseVisitor.cs
```

### Option 2: .NET Project with NuGet

Add to your `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Antlr4.Runtime.Standard" Version="4.13.1" />
  <PackageReference Include="Antlr4BuildTasks" Version="12.8.0" PrivateAssets="all" />
</ItemGroup>

<ItemGroup>
  <Antlr4 Include="Grammar\*.g4">
    <Visitor>true</Visitor>
    <Listener>false</Listener>
  </Antlr4>
</ItemGroup>
```

## Usage Example

```csharp
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

public class BmmdlCompiler
{
    public void Parse(string source)
    {
        var inputStream = new AntlrInputStream(source);
        var lexer = new BmmdlLexer(inputStream);
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new BmmdlParser(tokenStream);
        
        // Parse the source
        var tree = parser.compilationUnit();
        
        // Visit the parse tree
        var visitor = new BmmdlModelBuilder();
        var model = visitor.Visit(tree);
    }
}
```

## Grammar Coverage

### Definitions
- ✅ Entity, Type, Aspect, Service, Context
- ✅ View (with full SQL-like syntax)
- ✅ Extension, Modification, Annotate

### Advanced Features
- ✅ Access Control (Row & Field-level security)
- ✅ Business Rules (Validation, Compute)
- ✅ Sequences (Auto-number patterns)

### SQL Features
- ✅ SELECT, FROM, WHERE, GROUP BY, HAVING, ORDER BY
- ✅ JOINs (INNER, LEFT, RIGHT, FULL, CROSS)
- ✅ UNION, INTERSECT, EXCEPT
- ✅ Window Functions (RANK, ROW_NUMBER, LAG, LEAD)
- ✅ Aggregate Functions (SUM, AVG, COUNT, MIN, MAX)
- ✅ CASE expressions
- ✅ Subqueries

## Version

- Grammar Version: 3.0.0
- ANTLR Version: 4.13.1
- Target: C# (.NET 6.0+)
