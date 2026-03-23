namespace BMMDL.Runtime.Api.Services;

using BMMDL.MetaModel.Structure;

/// <summary>
/// Interface for entity data validation: enum values, JSONB structure.
/// Note: Static methods (ValidateRequiredAssociations, ValidateComputeFieldReferences, StripComputedFields)
/// remain on the EntityValidationService class and are not part of this interface.
/// </summary>
public interface IEntityValidationService
{
    /// <summary>
    /// Validate enum field values against their BmEnum definition.
    /// Returns error message if validation fails, null if OK.
    /// </summary>
    Task<string?> ValidateEnumFieldsAsync(BmEntity entityDef, Dictionary<string, object?> data);

    /// <summary>
    /// Validate JSONB fields against their BmType definition.
    /// Checks that JSON keys match BmType.Fields and required fields are present.
    /// Returns error message if validation fails, null if OK.
    /// </summary>
    Task<string?> ValidateJsonbFieldsAsync(BmEntity entityDef, Dictionary<string, object?> data);
}
