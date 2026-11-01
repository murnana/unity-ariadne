using UnityAriadne.Analyzer.Json;
using YamlDotNet.RepresentationModel;

namespace UnityAriadne.Analyzer.Yaml;

/// <summary>
/// Walks a document's node tree and extracts every object reference with its
/// property path (Phase 1b). Classification into ref_kind happens in Phase 1c.
/// </summary>
public static class ReferenceExtractor
{
    public static IReadOnlyList<UnityRef> Extract(UnityYamlDocument document)
    {
        // Start below the class-name key so paths read "m_Script", not "MonoBehaviour.m_Script".
        return ((YamlNode?)document.Properties ?? document.Root) is { } start
            ? Extract(start)
            : [];
    }

    /// <summary>
    /// Extracts from an arbitrary node tree. Used directly for non-asset YAML (.meta
    /// files, Phase 1d), where paths keep the top-level key ("ModelImporter.…").
    /// </summary>
    public static IReadOnlyList<UnityRef> Extract(YamlNode root)
    {
        var refs = new List<UnityRef>();
        Walk(root, path: "", refs);
        return refs;
    }

    private static void Walk(YamlNode node, string path, List<UnityRef> refs)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                if (TryParseReference(mapping, path) is { } reference)
                {
                    refs.Add(reference);
                    return;
                }

                // Addressables serializes asset GUIDs as bare 32-hex scalars, not the
                // {fileID, guid} mapping shape — recognized by their surrounding keys.
                if (TryParseBareGuidReference(mapping, path) is { } bareGuidRef)
                {
                    refs.Add(bareGuidRef);
                }

                foreach (var (key, value) in mapping.Children)
                {
                    var name = (key as YamlScalarNode)?.Value ?? key.ToString();
                    Walk(value, path.Length == 0 ? name : $"{path}.{name}", refs);
                }

                break;

            case YamlSequenceNode sequence:
                for (var i = 0; i < sequence.Children.Count; i++)
                {
                    Walk(sequence.Children[i], $"{path}[{i}]", refs);
                }

                break;

            // VFX assets smuggle JSON refs inside string scalars
            // (m_SerializableObject, Phase 1g) — invisible to the mapping shape above.
            case YamlScalarNode { Value: { } text }:
                refs.AddRange(JsonRefScanner.Scan(text, path));
                break;
        }
    }

    /// <summary>
    /// Addressables' two bare-GUID shapes (backlog; formats confirmed against real
    /// Unity 6000.0 + Addressables 2.3.16 output in fixtures/Addressables):
    /// an AddressableAssetGroup entry (<c>m_GUID</c> 32-hex scalar with an
    /// <c>m_Address</c> sibling — the sibling requirement excludes the group's own
    /// <c>m_GUID</c> identity field) and a serialized AssetReference
    /// (<c>m_AssetGUID</c> 32-hex scalar; empty when unset). FileId is 0: the target
    /// is the asset as a whole. The mapping is still recursed into afterwards.
    /// </summary>
    private static UnityRef? TryParseBareGuidReference(YamlMappingNode mapping, string path)
    {
        string? entryGuid = null;
        string? assetGuid = null;
        var hasAddress = false;

        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            if (keyNode is not YamlScalarNode { Value: { } key } || valueNode is not YamlScalarNode scalar)
            {
                continue;
            }

            switch (key)
            {
                case "m_GUID" when IsGuid(scalar.Value):
                    entryGuid = scalar.Value;
                    break;
                case "m_Address":
                    hasAddress = true;
                    break;
                case "m_AssetGUID" when IsGuid(scalar.Value):
                    assetGuid = scalar.Value;
                    break;
            }
        }

        if (assetGuid is not null)
        {
            return new UnityRef
            {
                FileId = 0,
                Guid = assetGuid,
                PropertyPath = Append(path, "m_AssetGUID"),
                FromAssetReference = true,
            };
        }

        if (entryGuid is not null && hasAddress)
        {
            return new UnityRef
            {
                FileId = 0,
                Guid = entryGuid,
                PropertyPath = Append(path, "m_GUID"),
                FromAddressableEntry = true,
            };
        }

        return null;

        static string Append(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";
    }

    private static bool IsGuid(string? value) =>
        value is { Length: 32 } && value.All(Uri.IsHexDigit);

    /// <summary>
    /// A reference is a mapping whose keys are a subset of {fileID, guid, type} with
    /// fileID present — the exact shape Unity serializes. Anything else (m_LocalRotation
    /// {x,y,z,w}, modification entries, ...) is an ordinary mapping to recurse into.
    /// </summary>
    private static UnityRef? TryParseReference(YamlMappingNode mapping, string path)
    {
        long? fileId = null;
        string? guid = null;
        int? type = null;

        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            if (keyNode is not YamlScalarNode { Value: { } key }
                || valueNode is not YamlScalarNode { Value: { } value })
            {
                return null;
            }

            switch (key)
            {
                case "fileID" when long.TryParse(value, out var parsedFileId):
                    fileId = parsedFileId;
                    break;
                case "guid":
                    guid = value;
                    break;
                case "type" when int.TryParse(value, out var parsedType):
                    type = parsedType;
                    break;
                default:
                    return null;
            }
        }

        if (fileId is not { } id)
        {
            return null;
        }

        return new UnityRef
        {
            FileId = id,
            Guid = guid,
            Type = type,
            PropertyPath = path,
        };
    }
}
