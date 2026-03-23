using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace BMMDL.Compiler.Pipeline;

/// <summary>
/// In-memory compilation cache that maps source file hashes to parsed results.
/// Avoids re-parsing unchanged files during incremental compilation.
/// Thread-safe for concurrent access.
/// </summary>
public class CompilationCache
{
    private readonly ConcurrentDictionary<string, CachedParseResult> _parseCache = new();

    /// <summary>
    /// Compute a SHA256 hash of the source content.
    /// </summary>
    public static string ComputeHash(string sourceContent)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourceContent));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Try to get a cached parse result for the given file name and source hash.
    /// Returns true if a cache hit was found and the result is still valid.
    /// </summary>
    public bool TryGetParseResult(string fileName, string sourceHash, out CachedParseResult? result)
    {
        var cacheKey = $"{fileName}:{sourceHash}";
        if (_parseCache.TryGetValue(cacheKey, out result))
        {
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>
    /// Store a parse result in the cache.
    /// </summary>
    public void StoreParseResult(string fileName, string sourceHash, CommonTokenStream tokenStream, IParseTree parseTree)
    {
        var cacheKey = $"{fileName}:{sourceHash}";
        _parseCache[cacheKey] = new CachedParseResult
        {
            FileName = fileName,
            SourceHash = sourceHash,
            TokenStream = tokenStream,
            ParseTree = parseTree,
            CachedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Remove all cached entries for a specific file.
    /// </summary>
    public void Invalidate(string fileName)
    {
        var keysToRemove = _parseCache.Keys
            .Where(k => k.StartsWith($"{fileName}:", StringComparison.Ordinal))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _parseCache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Clear the entire cache.
    /// </summary>
    public void Clear()
    {
        _parseCache.Clear();
    }

    /// <summary>
    /// Number of entries in the cache.
    /// </summary>
    public int Count => _parseCache.Count;
}

/// <summary>
/// A cached parse result containing token stream and parse tree for a single file.
/// </summary>
public class CachedParseResult
{
    public string FileName { get; init; } = "";
    public string SourceHash { get; init; } = "";
    public CommonTokenStream TokenStream { get; init; } = null!;
    public IParseTree ParseTree { get; init; } = null!;
    public DateTime CachedAt { get; init; }
}
