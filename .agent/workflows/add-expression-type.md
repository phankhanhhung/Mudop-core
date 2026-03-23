---
description: Thêm expression type mới cho computed fields và rules
---

# Add New Expression Type

Thêm một loại expression mới vào BMMDL (ví dụ: `BmTernaryExpression`, `BmLambdaExpression`).

## Files cần sửa

1. `src/BMMDL.MetaModel/Expressions/BmExpression.cs` - Định nghĩa class
2. `src/BMMDL.Compiler/Parsing/BmExpressionBuilder.cs` - Parse từ ANTLR
3. `src/BMMDL.CodeGen/PostgresSqlExpressionVisitor.cs` - Translate sang SQL
4. `src/BMMDL.Tests/Parser/BmExpressionBuilderTests.cs` - Parser tests
5. `src/BMMDL.Tests/CodeGen/ExpressionTranslatorTests.cs` - SQL tests

## Steps

### 1. Định nghĩa Expression class mới
Mở `src/BMMDL.MetaModel/Expressions/BmExpression.cs`, thêm:
```csharp
public sealed class BmYourExpression : BmExpression
{
    public BmExpression Operand { get; set; }
    // Thêm properties cần thiết
    
    public override IEnumerable<BmExpression> Children
    {
        get { yield return Operand; }
    }
    
    public override string ToExpressionString()
    {
        return $"your_expr({Operand.ToExpressionString()})";
    }
}
```

### 2. Cập nhật Parser (BmExpressionBuilder)
Mở `src/BMMDL.Compiler/Parsing/BmExpressionBuilder.cs`:
```csharp
public override BmExpression VisitYourExpressionContext(YourExpressionContext ctx)
{
    return new BmYourExpression
    {
        Operand = Visit(ctx.operand())
    };
}
```

### 3. Thêm SQL translation
Mở `src/BMMDL.CodeGen/PostgresSqlExpressionVisitor.cs`:
```csharp
public override string VisitBmYourExpression(BmYourExpression expr)
{
    var operand = Visit(expr.Operand);
    return $"YOUR_SQL_FUNCTION({operand})";
}
```

### 4. Thêm parser tests
Mở `src/BMMDL.Tests/Parser/BmExpressionBuilderTests.cs`:
```csharp
[Fact]
public void Parse_YourExpression_ReturnsCorrectNode()
{
    var source = @"entity Test { value: Integer computed your_expr(10); }";
    var model = BmmdlCompiler.QuickParse(source);
    
    var field = model.Entities.First().Fields.First(f => f.Name == "value");
    field.ComputedExpression.Should().BeOfType<BmYourExpression>();
}
```

### 5. Thêm CodeGen tests
Mở `src/BMMDL.Tests/CodeGen/ExpressionTranslatorTests.cs`:
```csharp
[Fact]
public void Translate_YourExpression_GeneratesCorrectSql()
{
    var expr = new BmYourExpression { Operand = new BmLiteralExpression(10) };
    var visitor = new PostgresSqlExpressionVisitor();
    
    var sql = visitor.Visit(expr);
    
    sql.Should().Be("YOUR_SQL_FUNCTION(10)");
}
```

### 6. Build và test
// turbo
```powershell
dotnet build BMMDL.sln /filelogger /flp:logfile=artifacts/build_log.txt
```

// turbo
```powershell
dotnet test src/BMMDL.Tests --filter "FullyQualifiedName~Expression" --logger "trx;LogFileName=expression_tests.trx" --results-directory artifacts
```
