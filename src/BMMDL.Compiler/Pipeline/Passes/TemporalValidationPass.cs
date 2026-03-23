using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Types;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 4.95: Temporal Validation
/// Validates temporal annotations for correctness and completeness.
/// 
/// This pass checks:
/// - @Temporal.ValidTime must have valid from/to column references
/// - ValidTime columns must exist and be of Date/DateTime type
/// - Reserved column names (system_start, system_end, version) cannot be used
/// - Temporal entities have valid key configurations
/// - Strategy values are valid ('inline' or 'separate')
/// </summary>
public class TemporalValidationPass : ICompilerPass
{
    public string Name => "Temporal Validation";
    public string Description => "Validate temporal annotations";
    public int Order => 50; // After FileStorageValidation (49), before SemanticValidation (51)

    // Reserved column names for temporal system columns
    private static readonly HashSet<string> ReservedTemporalColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "system_start", "system_end", "version",
        "systemstart", "systemend",
        "sys_start", "sys_end",
        "transaction_start", "transaction_end"
    };

    // Valid temporal column types
    private static readonly HashSet<string> ValidTemporalTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Date", "DateTime", "Timestamp", "TimestampWithTimeZone",
        "DateTimeOffset", "Instant"
    };

    // Valid strategy values
    private static readonly HashSet<string> ValidStrategyValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "inline", "separate", "inlinehistory", "separatetables"
    };

    public bool Execute(CompilationContext context)
    {
        if (context.Model == null)
        {
            context.AddError(ErrorCodes.SEM_NO_MODEL, "No model available for temporal validation", pass: Name);
            return false;
        }

        var model = context.Model;
        bool success = true;
        int validations = 0;

        foreach (var entity in model.Entities)
        {
            validations += ValidateTemporalEntity(context, entity, ref success);
        }

        // Update context metrics
        context.ValidationsPerformed += validations;

        return success;
    }

    private int ValidateTemporalEntity(CompilationContext context, BmEntity entity, ref bool success)
    {
        int count = 0;

        // Only validate if entity has temporal annotations
        if (!entity.IsTemporal && !entity.HasValidTime)
        {
            // Non-temporal entities can use 'version' and other reserved names
            // These are only reserved to avoid conflicts in temporal entities
            return count;
        }

        // ================================================================
        // @Temporal annotation validations
        // ================================================================
        if (entity.IsTemporal)
        {
            count += ValidateTemporalAnnotation(context, entity, ref success);
            count += ValidateTemporalKeyFields(context, entity, ref success);
            count += ValidateTemporalUniqueIndexes(context, entity, ref success);
        }

        // ================================================================
        // @Temporal.ValidTime annotation validations
        // ================================================================
        if (entity.HasValidTime)
        {
            count += ValidateValidTimeAnnotation(context, entity, ref success);
        }

        // ================================================================
        // Bitemporal-specific validations
        // ================================================================
        if (entity.IsBitemporal)
        {
            count += ValidateBitemporalConfiguration(context, entity, ref success);
        }

        // ================================================================
        // SeparateTables-specific validations
        // ================================================================
        if (entity.TemporalStrategy == TemporalStrategy.SeparateTables)
        {
            count += ValidateSeparateTablesRequirements(context, entity, ref success);
        }

        // Check reserved columns
        count += ValidateNoReservedColumns(context, entity, ref success);

        return count;
    }

    /// <summary>
    /// TEMP007: Validate @Temporal strategy value
    /// </summary>
    private int ValidateTemporalAnnotation(CompilationContext context, BmEntity entity, ref bool success)
    {
        int count = 1;

        var annotation = entity.GetAnnotation("Temporal");
        if (annotation == null) return count;

        var strategyValue = annotation.GetValue("strategy")?.ToString();
        if (!string.IsNullOrEmpty(strategyValue))
        {
            if (!ValidStrategyValues.Contains(strategyValue))
            {
                context.AddWarning(
                    ErrorCodes.TEMP_INVALID_STRATEGY,
                    $"Entity '{entity.Name}' has invalid @Temporal strategy '{strategyValue}'. Valid values are 'inline' or 'separate'. Defaulting to 'inline'.",
                    entity.SourceFile,
                    entity.StartLine,
                    Name
                );
            }
        }

        return count;
    }

    /// <summary>
    /// TEMP001, TEMP002, TEMP003, TEMP004, TEMP005, TEMP012: Validate @Temporal.ValidTime
    /// </summary>
    private int ValidateValidTimeAnnotation(CompilationContext context, BmEntity entity, ref bool success)
    {
        int count = 0;

        var annotation = entity.GetAnnotation("Temporal.ValidTime");
        if (annotation == null) return count;

        var fromColumn = annotation.GetValue("from")?.ToString();
        var toColumn = annotation.GetValue("to")?.ToString();

        count++;

        // TEMP001: Missing 'from' property
        if (string.IsNullOrEmpty(fromColumn))
        {
            context.AddError(
                ErrorCodes.TEMP_VALIDTIME_MISSING_FROM,
                $"Entity '{entity.Name}' has @Temporal.ValidTime but missing required 'from' property",
                entity.SourceFile,
                entity.StartLine,
                Name
            );
            success = false;
        }
        else
        {
            // TEMP003: 'from' column not found
            var fromField = entity.Fields.FirstOrDefault(f => 
                f.Name.Equals(fromColumn, StringComparison.OrdinalIgnoreCase));
            
            if (fromField == null)
            {
                context.AddError(
                    ErrorCodes.TEMP_VALIDTIME_FROM_NOT_FOUND,
                    $"Entity '{entity.Name}': @Temporal.ValidTime 'from' column '{fromColumn}' not found in entity fields",
                    entity.SourceFile,
                    entity.StartLine,
                    Name
                );
                success = false;
            }
            else
            {
                // TEMP005: Wrong type for 'from' column
                if (!IsValidTemporalType(fromField))
                {
                    context.AddError(
                        ErrorCodes.TEMP_VALIDTIME_WRONG_TYPE,
                        $"Entity '{entity.Name}': @Temporal.ValidTime 'from' column '{fromColumn}' must be of Date, DateTime, or Timestamp type (got '{GetFieldTypeName(fromField)}')",
                        entity.SourceFile,
                        fromField.StartLine > 0 ? fromField.StartLine : entity.StartLine,
                        Name
                    );
                    success = false;
                }

                // TEMP010: Nullable warning
                if (fromField.IsNullable)
                {
                    context.AddWarning(
                        ErrorCodes.TEMP_VALIDTIME_NULLABLE,
                        $"Entity '{entity.Name}': @Temporal.ValidTime 'from' column '{fromColumn}' should not be nullable for proper temporal semantics",
                        entity.SourceFile,
                        fromField.StartLine > 0 ? fromField.StartLine : entity.StartLine,
                        Name
                    );
                }
            }
            count++;
        }

        // TEMP002: Missing 'to' property
        if (string.IsNullOrEmpty(toColumn))
        {
            context.AddError(
                ErrorCodes.TEMP_VALIDTIME_MISSING_TO,
                $"Entity '{entity.Name}' has @Temporal.ValidTime but missing required 'to' property",
                entity.SourceFile,
                entity.StartLine,
                Name
            );
            success = false;
        }
        else
        {
            // TEMP004: 'to' column not found
            var toField = entity.Fields.FirstOrDefault(f => 
                f.Name.Equals(toColumn, StringComparison.OrdinalIgnoreCase));
            
            if (toField == null)
            {
                context.AddError(
                    ErrorCodes.TEMP_VALIDTIME_TO_NOT_FOUND,
                    $"Entity '{entity.Name}': @Temporal.ValidTime 'to' column '{toColumn}' not found in entity fields",
                    entity.SourceFile,
                    entity.StartLine,
                    Name
                );
                success = false;
            }
            else
            {
                // TEMP005: Wrong type for 'to' column
                if (!IsValidTemporalType(toField))
                {
                    context.AddError(
                        ErrorCodes.TEMP_VALIDTIME_WRONG_TYPE,
                        $"Entity '{entity.Name}': @Temporal.ValidTime 'to' column '{toColumn}' must be of Date, DateTime, or Timestamp type (got '{GetFieldTypeName(toField)}')",
                        entity.SourceFile,
                        toField.StartLine > 0 ? toField.StartLine : entity.StartLine,
                        Name
                    );
                    success = false;
                }

                // TEMP010: Nullable warning
                if (toField.IsNullable)
                {
                    context.AddWarning(
                        ErrorCodes.TEMP_VALIDTIME_NULLABLE,
                        $"Entity '{entity.Name}': @Temporal.ValidTime 'to' column '{toColumn}' should not be nullable for proper temporal semantics",
                        entity.SourceFile,
                        toField.StartLine > 0 ? toField.StartLine : entity.StartLine,
                        Name
                    );
                }
            }
            count++;
        }

        // TEMP012: from and to cannot be the same column
        if (!string.IsNullOrEmpty(fromColumn) && !string.IsNullOrEmpty(toColumn) &&
            fromColumn.Equals(toColumn, StringComparison.OrdinalIgnoreCase))
        {
            context.AddError(
                ErrorCodes.TEMP_VALIDTIME_SAME_COLUMN,
                $"Entity '{entity.Name}': @Temporal.ValidTime 'from' and 'to' cannot reference the same column '{fromColumn}'",
                entity.SourceFile,
                entity.StartLine,
                Name
            );
            success = false;
            count++;
        }

        return count;
    }

    /// <summary>
    /// TEMP008: Validate temporal entity key fields
    /// </summary>
    private int ValidateTemporalKeyFields(CompilationContext context, BmEntity entity, ref bool success)
    {
        int count = 0;

        foreach (var field in entity.Fields.Where(f => f.IsKey))
        {
            count++;

            // TEMP008: Key field cannot be computed
            if (field.IsComputed)
            {
                context.AddError(
                    ErrorCodes.TEMP_KEY_COMPUTED,
                    $"Entity '{entity.Name}': Temporal entity key field '{field.Name}' cannot have a computed expression",
                    entity.SourceFile,
                    field.StartLine > 0 ? field.StartLine : entity.StartLine,
                    Name
                );
                success = false;
            }
        }

        return count;
    }

    /// <summary>
    /// TEMP011: Warn about unique indexes on temporal entities
    /// </summary>
    private int ValidateTemporalUniqueIndexes(CompilationContext context, BmEntity entity, ref bool success)
    {
        int count = 0;

        foreach (var index in entity.Indexes.Where(idx => idx.IsUnique))
        {
            count++;

            // Check if index already includes system time consideration
            // For InlineHistory: should consider system_end = 'infinity' filter
            // For SeparateTables: main table only has current records, so OK
            if (entity.TemporalStrategy == TemporalStrategy.InlineHistory)
            {
                context.AddWarning(
                    ErrorCodes.TEMP_UNIQUE_INDEX_WARNING,
                    $"Entity '{entity.Name}': Unique index '{index.Name}' on InlineHistory temporal entity may need to consider system_end column for proper temporal uniqueness. Consider using a partial unique index with WHERE system_end = 'infinity'.",
                    entity.SourceFile,
                    entity.StartLine,
                    Name
                );
            }
        }

        return count;
    }

    /// <summary>
    /// TEMP009: Validate bitemporal configuration
    /// </summary>
    private int ValidateBitemporalConfiguration(CompilationContext context, BmEntity entity, ref bool success)
    {
        int count = 1;

        // TEMP009: Warn about bitemporal + SeparateTables complexity
        if (entity.TemporalStrategy == TemporalStrategy.SeparateTables)
        {
            context.AddWarning(
                ErrorCodes.TEMP_BITEMPORAL_SEPARATE_WARNING,
                $"Entity '{entity.Name}': Bitemporal entity with SeparateTables strategy may lead to complex query patterns. Consider using InlineHistory for simpler temporal queries.",
                entity.SourceFile,
                entity.StartLine,
                Name
            );
        }

        return count;
    }

    /// <summary>
    /// Validate SeparateTables strategy requirements:
    /// - Entity must have key fields (needed for history table FK)
    /// - system_start/system_end fields will be auto-generated (informational)
    /// </summary>
    private int ValidateSeparateTablesRequirements(CompilationContext context, BmEntity entity, ref bool success)
    {
        int count = 1;

        // SeparateTables strategy requires key fields for the history table FK relationship
        var keyFields = entity.Fields.Where(f => f.IsKey).ToList();
        if (keyFields.Count == 0)
        {
            context.AddWarning(
                ErrorCodes.TEMP_SEPARATE_NO_KEY,
                $"Entity '{entity.Name}' uses SeparateTables temporal strategy but has no key fields. " +
                $"Key fields are needed for the history table foreign key relationship. " +
                $"The history table trigger may not function correctly without a primary key.",
                entity.SourceFile,
                entity.StartLine,
                Name
            );
        }

        return count;
    }

    /// <summary>
    /// TEMP006: Check for reserved column names
    /// </summary>
    private int ValidateNoReservedColumns(CompilationContext context, BmEntity entity, ref bool success)
    {
        int count = 0;

        foreach (var field in entity.Fields)
        {
            count++;

            if (ReservedTemporalColumns.Contains(field.Name))
            {
                context.AddError(
                    ErrorCodes.TEMP_RESERVED_COLUMN,
                    $"Entity '{entity.Name}': Field '{field.Name}' uses a reserved temporal column name. Reserved names: system_start, system_end, version. These are auto-generated for @Temporal entities.",
                    entity.SourceFile,
                    field.StartLine > 0 ? field.StartLine : entity.StartLine,
                    Name
                );
                success = false;
            }
        }

        return count;
    }

    /// <summary>
    /// Check if field type is valid for temporal columns
    /// </summary>
    private bool IsValidTemporalType(BmField field)
    {
        var typeName = GetFieldTypeName(field);
        return ValidTemporalTypes.Contains(typeName);
    }

    /// <summary>
    /// Get the type name from a field, handling both TypeRef and TypeString
    /// </summary>
    private string GetFieldTypeName(BmField field)
    {
        if (field.TypeRef != null)
        {
            // Handle different TypeRef subclasses
            return field.TypeRef switch
            {
                BmPrimitiveType pt => pt.Kind.ToString(),
                BmCustomTypeReference ct => ct.TypeName,
                BmEntityTypeReference et => et.EntityName,
                BmArrayType at => $"Array<{GetTypeRefName(at.ElementType)}>",
                BmLocalizedType lt => $"localized {GetTypeRefName(lt.InnerType)}",
                _ => field.TypeRef.ToTypeString()
            };
        }

        // Fallback to TypeString
        var typeString = field.TypeString;
        if (string.IsNullOrEmpty(typeString))
            return "";

        // Extract base type (before any parentheses for parameters)
        var parenIndex = typeString.IndexOf('(');
        if (parenIndex > 0)
            typeString = typeString.Substring(0, parenIndex);

        // Remove nullable indicator
        typeString = typeString.TrimEnd('?');

        return typeString;
    }

    private string GetTypeRefName(BmTypeReference typeRef)
    {
        return typeRef switch
        {
            BmPrimitiveType pt => pt.Kind.ToString(),
            BmCustomTypeReference ct => ct.TypeName,
            BmEntityTypeReference et => et.EntityName,
            _ => typeRef.ToTypeString()
        };
    }
}

