using System;
using System.Collections.Generic;
using System.Linq;
using BMMDL.CodeGen.Schema;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.CodeGen.Schema;

/// <summary>
/// Compares two schema snapshots and detects differences.
/// </summary>
public class SchemaDiffer
{
    /// <summary>
    /// Compares desired schema vs current schema.
    /// </summary>
    public SchemaDiff Compare(SchemaSnapshot desired, SchemaSnapshot current)
    {
        return Compare(desired, current, null);
    }

    /// <summary>
    /// Compares desired schema vs current schema with optional rename hints.
    /// Rename hints (keyed by table name) cause matched column drops to become
    /// RENAME COLUMN instead of destructive DROP+ADD.
    /// </summary>
    public SchemaDiff Compare(
        SchemaSnapshot desired,
        SchemaSnapshot current,
        Dictionary<string, List<(string OldName, string NewName)>>? renameHints)
    {
        var diff = new SchemaDiff();

        // Table-level changes
        diff.TablesToAdd = FindMissingTables(desired, current);
        var droppedTables = FindExtraTablesWithInfo(desired, current);
        diff.TablesToDrop = droppedTables.Select(t => t.FullyQualifiedName).ToList();
        diff.DroppedTableInfo = droppedTables;
        diff.TablesToModify = FindModifiedTables(desired, current, renameHints);

        return diff;
    }
    
    private List<TableInfo> FindMissingTables(SchemaSnapshot desired, SchemaSnapshot current)
    {
        return desired.Tables
            .Where(dt => !current.Tables.Any(ct => 
                ct.Name.Equals(dt.Name, StringComparison.OrdinalIgnoreCase) &&
                ct.Schema.Equals(dt.Schema, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
    
    /// <summary>
    /// [Obsolete] Use FindExtraTablesWithInfo instead for full TableInfo.
    /// </summary>
    [Obsolete("Use FindExtraTablesWithInfo instead which returns TableInfo objects")]
    private List<string> FindExtraTables(SchemaSnapshot desired, SchemaSnapshot current)
    {
        return current.Tables
            .Where(ct => !desired.Tables.Any(dt =>
                dt.Name.Equals(ct.Name, StringComparison.OrdinalIgnoreCase) &&
                dt.Schema.Equals(ct.Schema, StringComparison.OrdinalIgnoreCase)))
            .Select(t => t.FullyQualifiedName)
            .ToList();
    }

    private List<TableInfo> FindExtraTablesWithInfo(SchemaSnapshot desired, SchemaSnapshot current)
    {
        return current.Tables
            .Where(ct => !desired.Tables.Any(dt =>
                dt.Name.Equals(ct.Name, StringComparison.OrdinalIgnoreCase) &&
                dt.Schema.Equals(ct.Schema, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
    
    private List<TableChange> FindModifiedTables(
        SchemaSnapshot desired,
        SchemaSnapshot current,
        Dictionary<string, List<(string OldName, string NewName)>>? renameHints)
    {
        var changes = new List<TableChange>();

        foreach (var desiredTable in desired.Tables)
        {
            var currentTable = current.Tables.FirstOrDefault(ct =>
                ct.Name.Equals(desiredTable.Name, StringComparison.OrdinalIgnoreCase) &&
                ct.Schema.Equals(desiredTable.Schema, StringComparison.OrdinalIgnoreCase));

            if (currentTable != null)
            {
                // Look up rename hints for this table
                List<(string OldName, string NewName)>? tableRenames = null;
                renameHints?.TryGetValue(desiredTable.Name, out tableRenames);

                var tableChange = CompareTable(desiredTable, currentTable, tableRenames);
                if (tableChange.HasChanges)
                {
                    changes.Add(tableChange);
                }
            }
        }

        return changes;
    }

    private TableChange CompareTable(
        TableInfo desired,
        TableInfo current,
        List<(string OldName, string NewName)>? renameHints)
    {
        var change = new TableChange
        {
            TableName = desired.Name,
            Schema = desired.Schema
        };

        // Column changes
        change.ColumnsToAdd = FindMissingColumns(desired, current);
        var droppedColumns = FindExtraColumnsWithInfo(desired, current);
        change.ColumnsToDrop = droppedColumns.Select(c => c.Name).ToList();
        change.DroppedColumnInfo = droppedColumns;
        change.ColumnsToModify = FindModifiedColumns(desired, current);

        // Apply rename hints: convert matched DROP+ADD pairs into renames
        if (renameHints != null)
        {
            foreach (var (oldName, newName) in renameHints)
            {
                var droppedMatch = change.ColumnsToDrop
                    .FirstOrDefault(c => c.Equals(oldName, StringComparison.OrdinalIgnoreCase));
                var addedMatch = change.ColumnsToAdd
                    .FirstOrDefault(c => c.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));

                if (droppedMatch != null && addedMatch != null)
                {
                    change.ColumnsToDrop.Remove(droppedMatch);
                    change.DroppedColumnInfo.RemoveAll(c => c.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
                    change.ColumnsToAdd.Remove(addedMatch);
                    change.ColumnRenames.Add((oldName, newName));
                }
            }
        }

        // Index changes
        change.IndexesToAdd = FindMissingIndexes(desired, current);
        change.IndexesToDrop = FindExtraIndexes(desired, current);

        // Constraint changes
        change.ConstraintsToAdd = FindMissingConstraints(desired, current);
        change.ConstraintsToDrop = FindExtraConstraints(desired, current);

        return change;
    }
    
    #region Column Comparison
    
    private List<ColumnInfo> FindMissingColumns(TableInfo desired, TableInfo current)
    {
        return desired.Columns
            .Where(dc => !current.Columns.Any(cc => 
                cc.Name.Equals(dc.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
    
    /// <summary>
    /// [Obsolete] Use FindExtraColumnsWithInfo instead for full ColumnInfo.
    /// </summary>
    [Obsolete("Use FindExtraColumnsWithInfo instead which returns ColumnInfo objects")]
    private List<string> FindExtraColumns(TableInfo desired, TableInfo current)
    {
        return current.Columns
            .Where(cc => !desired.Columns.Any(dc =>
                dc.Name.Equals(cc.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.Name)
            .ToList();
    }

    private List<ColumnInfo> FindExtraColumnsWithInfo(TableInfo desired, TableInfo current)
    {
        return current.Columns
            .Where(cc => !desired.Columns.Any(dc =>
                dc.Name.Equals(cc.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
    
    private List<ColumnModification> FindModifiedColumns(TableInfo desired, TableInfo current)
    {
        var modifications = new List<ColumnModification>();
        
        foreach (var desiredCol in desired.Columns)
        {
            var currentCol = current.Columns.FirstOrDefault(cc =>
                cc.Name.Equals(desiredCol.Name, StringComparison.OrdinalIgnoreCase));
            
            if (currentCol != null)
            {
                var mod = CompareColumn(desiredCol, currentCol);
                if (mod.Changes.Any())
                {
                    modifications.Add(mod);
                }
            }
        }
        
        return modifications;
    }
    
    private ColumnModification CompareColumn(ColumnInfo desired, ColumnInfo current)
    {
        var mod = new ColumnModification
        {
            ColumnName = desired.Name,
            OldDefinition = current,
            NewDefinition = desired
        };
        
        // Data type change
        if (!desired.DataType.Equals(current.DataType, StringComparison.OrdinalIgnoreCase))
        {
            mod.Changes.Add(ChangeType.DataTypeChange);
        }
        
        // Nullability change
        if (desired.IsNullable != current.IsNullable)
        {
            mod.Changes.Add(ChangeType.NullabilityChange);
        }
        
        // Default value change
        if (desired.DefaultValue != current.DefaultValue)
        {
            mod.Changes.Add(ChangeType.DefaultValueChange);
        }
        
        // Computed expression change
        if (desired.IsGenerated != current.IsGenerated ||
            desired.GenerationExpression != current.GenerationExpression)
        {
            mod.Changes.Add(ChangeType.ComputedExpressionChange);
        }
        
        return mod;
    }
    
    #endregion
    
    #region Index Comparison
    
    private List<IndexInfo> FindMissingIndexes(TableInfo desired, TableInfo current)
    {
        return desired.Indexes
            .Where(di => !current.Indexes.Any(ci => 
                IndexesMatch(di, ci)))
            .ToList();
    }
    
    private List<string> FindExtraIndexes(TableInfo desired, TableInfo current)
    {
        return current.Indexes
            .Where(ci => !desired.Indexes.Any(di => 
                IndexesMatch(di, ci)))
            .Select(i => i.Name)
            .ToList();
    }
    
    private bool IndexesMatch(IndexInfo a, IndexInfo b)
    {
        return a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase) ||
               (a.IsUnique == b.IsUnique && 
                a.IsPrimary == b.IsPrimary &&
                a.Columns.SequenceEqual(b.Columns, StringComparer.OrdinalIgnoreCase));
    }
    
    #endregion
    
    #region Constraint Comparison
    
    private List<ConstraintInfo> FindMissingConstraints(TableInfo desired, TableInfo current)
    {
        return desired.Constraints
            .Where(dc => !current.Constraints.Any(cc => 
                ConstraintsMatch(dc, cc)))
            .ToList();
    }
    
    private List<string> FindExtraConstraints(TableInfo desired, TableInfo current)
    {
        return current.Constraints
            .Where(cc => !desired.Constraints.Any(dc => 
                ConstraintsMatch(dc, cc)))
            .Select(c => c.Name)
            .ToList();
    }
    
    private bool ConstraintsMatch(ConstraintInfo a, ConstraintInfo b)
    {
        if (a.Type != b.Type)
            return false;
        
        if (!a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase))
            return false;
        
        // For FK constraints, also check referenced table
        if (a.Type == ConstraintType.ForeignKey)
        {
            return a.ReferencedTable?.Equals(b.ReferencedTable, StringComparison.OrdinalIgnoreCase) == true;
        }
        
        // For EXCLUDE constraints (temporal), just match by name since definitions are complex
        if (a.Type == ConstraintType.Exclude)
        {
            return true; // Name match is sufficient for EXCLUDE constraints
        }
        
        return true;
    }
    
    #endregion

    #region Rename Hint Extraction

    /// <summary>
    /// Extract column rename hints from model.Modifications for use with Compare().
    /// Returns a dictionary keyed by table name (snake_case) with lists of (OldColumnName, NewColumnName).
    /// </summary>
    public static Dictionary<string, List<(string OldName, string NewName)>> ExtractRenameHints(BmModel model)
    {
        return model.Modifications
            .Where(m => m.TargetKind == "entity")
            .SelectMany(m => m.Actions.OfType<BmRenameFieldAction>()
                .Select(a => (
                    Table: NamingConvention.GetTableName(m.TargetName),
                    Old: NamingConvention.GetColumnName(a.OldName),
                    New: NamingConvention.GetColumnName(a.NewName))))
            .GroupBy(x => x.Table)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => (x.Old, x.New)).ToList());
    }

    #endregion
}
