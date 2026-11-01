namespace UnityAriadne.Analyzer.Scanning;

/// <summary>One resolved GUID: the asset it points to and how it was found.</summary>
/// <param name="Guid">32-char lowercase hex GUID from the .meta file.</param>
/// <param name="Path">Asset path relative to the project root, with '/' separators.</param>
/// <param name="AssetType">See <see cref="AssetTypeClassifier"/>.</param>
/// <param name="Source">"Assets", "Packages" or "PackageCache".</param>
public sealed record AssetEntry(string Guid, string Path, string AssetType, string Source);

public sealed record ScanWarning(string MetaPath, string Message);

/// <summary>
/// GUID → asset lookup built by <see cref="UnityProjectScanner"/> (Phase 1a).
/// </summary>
public sealed class GuidDictionary
{
    private readonly Dictionary<string, AssetEntry> _byGuid;
    private readonly List<ScanWarning> _warnings;

    internal GuidDictionary(Dictionary<string, AssetEntry> byGuid, List<ScanWarning> warnings)
    {
        _byGuid = byGuid;
        _warnings = warnings;
    }

    public int Count => _byGuid.Count;

    public IReadOnlyCollection<AssetEntry> Entries => _byGuid.Values;

    public IReadOnlyList<ScanWarning> Warnings => _warnings;

    public AssetEntry? Find(string guid) => _byGuid.GetValueOrDefault(guid);

    public bool TryResolve(string guid, out AssetEntry entry)
    {
        if (_byGuid.TryGetValue(guid, out var found))
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }
}
