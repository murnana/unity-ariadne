namespace UnityAriadne.Analyzer.Scanning;

/// <summary>
/// Walks a Unity project's .meta files and builds the GUID dictionary (Phase 1a).
/// </summary>
public static class UnityProjectScanner
{
    // Scan order doubles as priority: on duplicate GUIDs the first hit wins, so
    // project assets shadow package copies (matches Unity, which ignores the
    // Samples~ originals inside packages anyway).
    private static readonly (string RelativeDir, string Source)[] ScanRoots =
    [
        ("Assets", "Assets"),
        ("Packages", "Packages"),
        (Path.Combine("Library", "PackageCache"), "PackageCache"),
    ];

    /// <summary>
    /// Scans Assets/, Packages/ and Library/PackageCache/ under <paramref name="projectRoot"/>.
    /// Missing roots are skipped (CI checkouts have no Library/PackageCache).
    /// </summary>
    public static GuidDictionary Scan(string projectRoot)
    {
        var byGuid = new Dictionary<string, AssetEntry>(StringComparer.Ordinal);
        var warnings = new List<ScanWarning>();

        foreach (var (relativeDir, source) in ScanRoots)
        {
            var rootDir = Path.Combine(projectRoot, relativeDir);
            if (!Directory.Exists(rootDir))
            {
                continue;
            }

            var metaPaths = Directory
                .EnumerateFiles(rootDir, "*.meta", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal);

            foreach (var metaPath in metaPaths)
            {
                var relativeMetaPath = Path.GetRelativePath(projectRoot, metaPath).Replace('\\', '/');
                if (IsIgnoredByUnity(relativeMetaPath))
                {
                    continue;
                }

                MetaFileInfo? meta;
                try
                {
                    meta = MetaFileInfo.ParseFile(metaPath);
                }
                catch (IOException e)
                {
                    warnings.Add(new ScanWarning(relativeMetaPath, $"unreadable: {e.Message}"));
                    continue;
                }

                if (meta is null)
                {
                    warnings.Add(new ScanWarning(relativeMetaPath, "no guid found"));
                    continue;
                }

                var assetPath = relativeMetaPath[..^".meta".Length];
                var isFolder = meta.IsFolder || Directory.Exists(Path.Combine(projectRoot, assetPath));
                var assetType = AssetTypeClassifier.Classify(assetPath, isFolder, meta.ImporterType, meta.MainObjectFileId);
                var entry = new AssetEntry(meta.Guid, assetPath, assetType, source);

                if (!byGuid.TryAdd(meta.Guid, entry))
                {
                    warnings.Add(new ScanWarning(
                        relativeMetaPath,
                        $"duplicate guid {meta.Guid}, already mapped to {byGuid[meta.Guid].Path}"));
                }
            }
        }

        return new GuidDictionary(byGuid, warnings);
    }

    /// <summary>
    /// Unity does not import paths containing a segment that starts with '.' or ends
    /// with '~' (e.g. Samples~, Documentation~ inside packages). Their stale .meta
    /// files must be skipped or they collide with the imported copies under Assets/.
    /// </summary>
    internal static bool IsIgnoredByUnity(string relativePath)
    {
        foreach (var segment in relativePath.Split('/'))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            var name = segment.EndsWith(".meta", StringComparison.Ordinal)
                ? segment[..^".meta".Length]
                : segment;
            if (name.StartsWith('.') || name.EndsWith('~'))
            {
                return true;
            }
        }

        return false;
    }
}
