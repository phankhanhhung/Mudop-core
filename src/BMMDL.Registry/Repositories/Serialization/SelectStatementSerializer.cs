using BMMDL.MetaModel.Structure;

namespace BMMDL.Registry.Repositories.Serialization;

/// <summary>
/// Serializes/deserializes BmSelectStatement to/from JSON for view persistence.
/// All methods are static — no DB dependencies.
/// </summary>
internal static class SelectStatementSerializer
{
    public static string SerializeParsedSelect(BmSelectStatement select)
    {
        return System.Text.Json.JsonSerializer.Serialize(SelectStatementToDto(select));
    }

    public static BmSelectStatement? DeserializeParsedSelect(string json)
    {
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<ParsedSelectDto>(json);
            return dto != null ? DtoToSelectStatement(dto) : null;
        }
        catch
        {
            return null;
        }
    }

    private static ParsedSelectDto SelectStatementToDto(BmSelectStatement s) => new()
    {
        IsDistinct = s.IsDistinct,
        Columns = s.Columns.Select(c => new SelectColumnDto
        {
            ExpressionString = c.ExpressionString, Alias = c.Alias,
            IsWildcard = c.IsWildcard, WildcardQualifier = c.WildcardQualifier
        }).ToList(),
        From = FromSourceToDto(s.From),
        Joins = s.Joins.Select(j => new JoinClauseDto
        {
            JoinType = j.JoinType.ToString(), Source = FromSourceToDto(j.Source),
            OnConditionString = j.OnConditionString
        }).ToList(),
        WhereConditionString = s.WhereConditionString,
        GroupByStrings = s.GroupByStrings.ToList(),
        HavingConditionString = s.HavingConditionString,
        OrderByColumns = s.OrderByColumns.Select(o => new OrderByColumnDto
        {
            ExpressionString = o.ExpressionString, Direction = o.Direction.ToString(),
            NullsOrdering = o.NullsOrdering?.ToString()
        }).ToList(),
        UnionClauses = s.UnionClauses.Select(u => new UnionClauseDto
        {
            Type = u.Type.ToString(), IsAll = u.IsAll,
            Select = SelectStatementToDto(u.Select)
        }).ToList()
    };

    private static FromSourceDto FromSourceToDto(BmFromSource f) => new()
    {
        EntityReference = f.EntityReference, Alias = f.Alias,
        Subquery = f.Subquery != null ? SelectStatementToDto(f.Subquery) : null,
        TemporalRawText = f.TemporalQualifier?.RawText,
        TemporalType = f.TemporalQualifier?.Type.ToString()
    };

    private static BmSelectStatement DtoToSelectStatement(ParsedSelectDto dto)
    {
        var s = new BmSelectStatement { IsDistinct = dto.IsDistinct };
        foreach (var c in dto.Columns)
            s.Columns.Add(new BmSelectColumn
            {
                ExpressionString = c.ExpressionString, Alias = c.Alias,
                IsWildcard = c.IsWildcard, WildcardQualifier = c.WildcardQualifier
            });
        s.From = DtoToFromSource(dto.From);
        foreach (var j in dto.Joins)
            s.Joins.Add(new BmJoinClause
            {
                JoinType = Enum.TryParse<BmJoinType>(j.JoinType, true, out var jt) ? jt : BmJoinType.Inner,
                Source = DtoToFromSource(j.Source), OnConditionString = j.OnConditionString
            });
        s.WhereConditionString = dto.WhereConditionString;
        foreach (var g in dto.GroupByStrings) s.GroupByStrings.Add(g);
        s.HavingConditionString = dto.HavingConditionString;
        foreach (var o in dto.OrderByColumns)
            s.OrderByColumns.Add(new BmOrderByColumn
            {
                ExpressionString = o.ExpressionString,
                Direction = Enum.TryParse<BmSortDirection>(o.Direction, true, out var d) ? d : BmSortDirection.Asc,
                NullsOrdering = !string.IsNullOrEmpty(o.NullsOrdering) && Enum.TryParse<BmNullsOrdering>(o.NullsOrdering, true, out var no) ? no : null
            });
        foreach (var u in dto.UnionClauses)
            s.UnionClauses.Add(new BmUnionClause
            {
                Type = Enum.TryParse<BmUnionType>(u.Type, true, out var ut) ? ut : BmUnionType.Union,
                IsAll = u.IsAll, Select = DtoToSelectStatement(u.Select)
            });
        return s;
    }

    private static BmFromSource DtoToFromSource(FromSourceDto dto)
    {
        var f = new BmFromSource
        {
            EntityReference = dto.EntityReference, Alias = dto.Alias,
            Subquery = dto.Subquery != null ? DtoToSelectStatement(dto.Subquery) : null
        };
        if (!string.IsNullOrEmpty(dto.TemporalType))
            f.TemporalQualifier = new BmTemporalQualifier
            {
                Type = Enum.TryParse<BmTemporalQualifierType>(dto.TemporalType, true, out var tt) ? tt : BmTemporalQualifierType.Current,
                RawText = dto.TemporalRawText
            };
        return f;
    }

    // DTO classes
    private class ParsedSelectDto
    {
        public bool IsDistinct { get; set; }
        public List<SelectColumnDto> Columns { get; set; } = new();
        public FromSourceDto From { get; set; } = new();
        public List<JoinClauseDto> Joins { get; set; } = new();
        public string? WhereConditionString { get; set; }
        public List<string> GroupByStrings { get; set; } = new();
        public string? HavingConditionString { get; set; }
        public List<OrderByColumnDto> OrderByColumns { get; set; } = new();
        public List<UnionClauseDto> UnionClauses { get; set; } = new();
    }
    private class SelectColumnDto
    {
        public string ExpressionString { get; set; } = "";
        public string? Alias { get; set; }
        public bool IsWildcard { get; set; }
        public string? WildcardQualifier { get; set; }
    }
    private class FromSourceDto
    {
        public string? EntityReference { get; set; }
        public string? Alias { get; set; }
        public ParsedSelectDto? Subquery { get; set; }
        public string? TemporalRawText { get; set; }
        public string? TemporalType { get; set; }
    }
    private class JoinClauseDto
    {
        public string JoinType { get; set; } = "Inner";
        public FromSourceDto Source { get; set; } = new();
        public string? OnConditionString { get; set; }
    }
    private class OrderByColumnDto
    {
        public string ExpressionString { get; set; } = "";
        public string Direction { get; set; } = "Asc";
        public string? NullsOrdering { get; set; }
    }
    private class UnionClauseDto
    {
        public string Type { get; set; } = "Union";
        public bool IsAll { get; set; }
        public ParsedSelectDto Select { get; set; } = new();
    }
}
