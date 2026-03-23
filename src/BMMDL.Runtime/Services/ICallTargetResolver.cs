namespace BMMDL.Runtime.Services;

using BMMDL.MetaModel.Service;

/// <summary>
/// Resolves call statement targets to action/function definitions.
/// </summary>
public interface ICallTargetResolver
{
    /// <summary>
    /// Resolve a call target string to a function/action definition.
    /// Supports formats: "EntityName.ActionName", "ServiceName.ActionName", or bare "ActionName".
    /// When serviceName is provided, that service's actions are preferred for disambiguation.
    /// </summary>
    BmFunction? Resolve(string target, string? serviceName = null);
}
