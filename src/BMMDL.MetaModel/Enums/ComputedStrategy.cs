namespace BMMDL.MetaModel.Enums;

/// <summary>
/// Strategy for computed field generation
/// </summary>
public enum ComputedStrategy
{
    /// <summary>
    /// GENERATED ALWAYS AS (...) STORED - pre-calculated, stored on disk, indexable
    /// </summary>
    Stored,
    
    /// <summary>
    /// GENERATED ALWAYS AS (...) - calculated on read, no storage, not indexable
    /// </summary>
    Virtual,
    
    /// <summary>
    /// Regular column - application handles calculation
    /// </summary>
    Application,
    
    /// <summary>
    /// Database trigger - BEFORE INSERT/UPDATE trigger calculates value
    /// </summary>
    Trigger
}
