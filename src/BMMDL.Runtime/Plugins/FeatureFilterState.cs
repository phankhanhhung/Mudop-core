namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Stack-based implementation of <see cref="IFeatureFilterState"/>.
/// Supports nested scope overrides — each Disable/Enable pushes onto the stack,
/// and the returned IDisposable pops on dispose.
/// </summary>
public class FeatureFilterState : IFeatureFilterState
{
    private readonly Dictionary<string, Stack<bool>> _overrides = new();
    private readonly HashSet<string> _defaultEnabled;

    public FeatureFilterState(IEnumerable<string> enabledByDefault)
        => _defaultEnabled = new(enabledByDefault);

    public bool IsEnabled(string featureName)
    {
        if (_overrides.TryGetValue(featureName, out var stack) && stack.Count > 0)
            return stack.Peek();
        return _defaultEnabled.Contains(featureName);
    }

    public IDisposable Disable(string featureName)
    {
        EnsureStack(featureName);
        _overrides[featureName].Push(false);
        return new FilterScope(this, featureName);
    }

    public IDisposable Enable(string featureName)
    {
        EnsureStack(featureName);
        _overrides[featureName].Push(true);
        return new FilterScope(this, featureName);
    }

    private void EnsureStack(string featureName)
    {
        if (!_overrides.ContainsKey(featureName))
            _overrides[featureName] = new();
    }

    private sealed class FilterScope(FeatureFilterState state, string name) : IDisposable
    {
        public void Dispose() => state._overrides[name].Pop();
    }
}
