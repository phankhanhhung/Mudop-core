namespace BMMDL.Runtime.Api.Services;

using BMMDL.MetaModel.Structure;

/// <summary>
/// Interface for OData v4 $value property-level raw value access (GET/PUT/DELETE).
/// </summary>
public interface IPropertyValueService
{
    /// <summary>
    /// Get raw value of a property.
    /// </summary>
    Task<PropertyValueResult> GetPropertyValueAsync(
        BmEntity entityDef, Guid id, string property, Guid? tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Update raw value of a property.
    /// </summary>
    Task<PropertyValueResult> UpdatePropertyValueAsync(
        BmEntity entityDef, Guid id, string property, byte[] content, Guid? tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Delete (set to null) the value of a property.
    /// </summary>
    Task<PropertyValueResult> DeletePropertyValueAsync(
        BmEntity entityDef, Guid id, string property, Guid? tenantId,
        CancellationToken ct = default);
}
