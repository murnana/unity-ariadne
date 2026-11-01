namespace UnityAriadne.Analyzer.Scanning;

/// <summary>
/// The subset of a Unity <c>.meta</c> file needed to build the GUID dictionary.
/// </summary>
/// <remarks>
/// Parsed line-by-line instead of with a YAML library: only top-level keys matter here,
/// and .meta files whose importer settings are all defaults (common in Unity 6) omit the
/// importer section entirely, leaving just <c>fileFormatVersion</c>, <c>guid</c> and
/// <c>AssetOrigin</c>.
/// </remarks>
public sealed record MetaFileInfo
{
    public required string Guid { get; init; }

    /// <summary>True when the meta declares <c>folderAsset: yes</c>.</summary>
    public bool IsFolder { get; init; }

    /// <summary>First importer section name (e.g. <c>MonoImporter</c>), or null when omitted.</summary>
    public string? ImporterType { get; init; }

    /// <summary>NativeFormatImporter's <c>mainObjectFileID</c>, when present.</summary>
    public long? MainObjectFileId { get; init; }

    // Top-level keys that are metadata rather than an importer section.
    private static readonly HashSet<string> NonImporterKeys = new(StringComparer.Ordinal)
    {
        "fileFormatVersion", "guid", "folderAsset", "labels",
        "timeCreated", "licenseType", "AssetOrigin",
    };

    /// <summary>Parses a .meta file's content. Returns null when no top-level guid is found.</summary>
    public static MetaFileInfo? Parse(TextReader reader)
    {
        string? guid = null;
        var isFolder = false;
        string? importerType = null;
        long? mainObjectFileId = null;

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] is ' ' or '-' or '\t')
            {
                // Indented content. The only nested value we need is mainObjectFileID;
                // nested guids (e.g. ModelImporter externalObjects) must NOT be picked up.
                if (mainObjectFileId is null)
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("mainObjectFileID:", StringComparison.Ordinal)
                        && long.TryParse(trimmed["mainObjectFileID:".Length..], out var id))
                    {
                        mainObjectFileId = id;
                    }
                }

                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon];
            var value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "guid":
                    guid ??= value;
                    break;
                case "folderAsset":
                    isFolder = value == "yes";
                    break;
                default:
                    if (importerType is null && !NonImporterKeys.Contains(key))
                    {
                        importerType = key;
                    }

                    break;
            }
        }

        if (string.IsNullOrEmpty(guid))
        {
            return null;
        }

        return new MetaFileInfo
        {
            Guid = guid,
            IsFolder = isFolder,
            ImporterType = importerType,
            MainObjectFileId = mainObjectFileId,
        };
    }

    public static MetaFileInfo? ParseFile(string path)
    {
        using var reader = new StreamReader(path);
        return Parse(reader);
    }
}
