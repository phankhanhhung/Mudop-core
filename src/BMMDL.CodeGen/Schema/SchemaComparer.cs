namespace BMMDL.CodeGen.Schema;

/// <summary>
/// Compares two schema snapshots to produce a SchemaDiff for migration generation.
/// </summary>
public class SchemaComparer
{
    /// <summary>
    /// Compare current (live) schema with target schema to produce a diff.
    /// </summary>
    public SchemaDiff Compare(SchemaSnapshot current, SchemaSnapshot target)
    {
        return Compare(current, target, null);
    }

    /// <summary>
    /// Compare current (live) schema with target schema to produce a diff,
    /// with optional rename hints from model.Modifications.
    /// </summary>
    public SchemaDiff Compare(
        SchemaSnapshot current,
        SchemaSnapshot target,
        Dictionary<string, List<(string OldName, string NewName)>>? renameHints)
    {
        var diff = new SchemaDiff();

        var currentTables = current.Tables
            .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var targetTables = target.Tables
            .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        // Tables to add (exist in target but not in current)
        foreach (var kvp in targetTables)
        {
            if (!currentTables.ContainsKey(kvp.Key))
            {
                diff.TablesToAdd.Add(kvp.Value);
            }
        }

        // Tables to drop (exist in current but not in target)
        foreach (var kvp in currentTables)
        {
            if (!targetTables.ContainsKey(kvp.Key))
            {
                diff.TablesToDrop.Add(kvp.Value.FullyQualifiedName);
                diff.DroppedTableInfo.Add(kvp.Value);
            }
        }

        // Tables to modify (exist in both, compare columns)
        foreach (var kvp in targetTables)
        {
            if (currentTables.TryGetValue(kvp.Key, out var currentTable))
            {
                // Look up rename hints for this table
                List<(string OldName, string NewName)>? tableRenames = null;
                renameHints?.TryGetValue(kvp.Key, out tableRenames);

                var tableChange = CompareTable(currentTable, kvp.Value, tableRenames);
                if (tableChange.HasChanges)
                {
                    diff.TablesToModify.Add(tableChange);
                }
            }
        }

        return diff;
    }

    /// <summary>
    /// Build a target SchemaSnapshot from compiled BmModel entities.
    /// </summary>
    public SchemaSnapshot BuildTargetSnapshot(
        BMMDL.MetaModel.BmModel model,
        string schemaName,
        TypeResolver resolver)
    {
        var snapshot = new SchemaSnapshot
        {
            SchemaName = schemaName,
            ReadAt = DateTime.UtcNow
        };

        foreach (var entity in model.Entities)
        {
            var tableInfo = new TableInfo
            {
                Schema = schemaName,
                Name = BMMDL.MetaModel.Utilities.NamingConvention.GetTableName(entity)
            };

            // Add columns from entity fields
            foreach (var field in entity.Fields)
            {
                var resolved = resolver.Resolve(field);
                var colName = BMMDL.MetaModel.Utilities.NamingConvention.GetColumnName(field.Name);

                tableInfo.Columns.Add(new ColumnInfo
                {
                    Name = colName,
                    DataType = NormalizeDataType(resolved.PostgresType),
                    IsNullable = resolved.Nullable,
                    DefaultValue = resolved.DefaultValue,
                    IsGenerated = field.IsComputed &&
                        (field.ComputedStrategy == BMMDL.MetaModel.Enums.ComputedStrategy.Stored ||
                         field.ComputedStrategy == BMMDL.MetaModel.Enums.ComputedStrategy.Virtual)
                });
            }

            // Add FK columns from associations
            foreach (var assoc in entity.Associations)
            {
                if (assoc.Cardinality == BMMDL.MetaModel.Structure.BmCardinality.ManyToOne ||
                    assoc.Cardinality == BMMDL.MetaModel.Structure.BmCardinality.OneToOne)
                {
                    var fkCol = BMMDL.MetaModel.Utilities.NamingConvention.GetFkColumnName(assoc.Name);
                    if (!tableInfo.Columns.Any(c => c.Name.Equals(fkCol, StringComparison.OrdinalIgnoreCase)))
                    {
                        tableInfo.Columns.Add(new ColumnInfo
                        {
                            Name = fkCol,
                            DataType = "uuid",
                            IsNullable = assoc.MinCardinality < 1
                        });
                    }
                }
            }

            // Add tenant_id if tenant-scoped
            if (entity.TenantScoped &&
                !tableInfo.Columns.Any(c => c.Name.Equals("tenant_id", StringComparison.OrdinalIgnoreCase)))
            {
                tableInfo.Columns.Add(new ColumnInfo
                {
                    Name = "tenant_id",
                    DataType = "uuid",
                    IsNullable = false
                });
            }

            snapshot.Tables.Add(tableInfo);
        }

        return snapshot;
    }

    private TableChange CompareTable(
        TableInfo current,
        TableInfo target,
        List<(string OldName, string NewName)>? renameHints)
    {
        var change = new TableChange
        {
            TableName = target.FullyQualifiedName
        };

        var currentCols = current.Columns
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var targetCols = target.Columns
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        // Columns to add
        foreach (var kvp in targetCols)
        {
            if (!currentCols.ContainsKey(kvp.Key))
            {
                change.ColumnsToAdd.Add(kvp.Value);
            }
        }

        // Columns to drop
        foreach (var kvp in currentCols)
        {
            if (!targetCols.ContainsKey(kvp.Key))
            {
                change.ColumnsToDrop.Add(kvp.Key);
            }
        }

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
                    change.ColumnsToAdd.Remove(addedMatch);
                    change.ColumnRenames.Add((oldName, newName));
                }
            }
        }

        // Columns to modify (type or nullability change)
        foreach (var kvp in targetCols)
        {
            if (currentCols.TryGetValue(kvp.Key, out var currentCol))
            {
                var modifications = CompareColumn(currentCol, kvp.Value);
                if (modifications.Count > 0)
                {
                    change.ColumnsToModify.AddRange(modifications);
                }
            }
        }

        return change;
    }

    private List<ColumnModification> CompareColumn(ColumnInfo current, ColumnInfo target)
    {
        var mods = new List<ColumnModification>();
        var currentType = NormalizeDataType(current.DataType);
        var targetType = NormalizeDataType(target.DataType);

        var changes = new List<ChangeType>();

        if (!currentType.Equals(targetType, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add(ChangeType.DataTypeChange);
        }

        if (current.IsNullable != target.IsNullable)
        {
            changes.Add(ChangeType.NullabilityChange);
        }

        if (changes.Count > 0)
        {
            mods.Add(new ColumnModification
            {
                ColumnName = target.Name,
                OldDefinition = current,
                NewDefinition = target,
                Changes = changes
            });
        }

        return mods;
    }

    /// <summary>
    /// Normalize PostgreSQL data type names for comparison.
    /// information_schema reports types differently than DDL syntax.
    /// </summary>
    private static string NormalizeDataType(string dataType)
    {
        return dataType.ToLowerInvariant() switch
        {
            "character varying" => "varchar",
            "character" => "char",
            "integer" => "integer",
            "bigint" => "bigint",
            "smallint" => "smallint",
            "boolean" => "boolean",
            "numeric" => "numeric",
            "double precision" => "double precision",
            "real" => "real",
            "text" => "text",
            "date" => "date",
            "time without time zone" => "time",
            "timestamp without time zone" => "timestamp",
            "timestamp with time zone" => "timestamptz",
            "uuid" => "uuid",
            "bytea" => "bytea",
            "jsonb" => "jsonb",
            "json" => "json",
            _ => dataType.ToLowerInvariant()
        };
    }
}
