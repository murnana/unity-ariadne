using UnityAriadne.Analyzer.Scanning;
using UnityAriadne.Analyzer.Yaml;
using YamlDotNet.RepresentationModel;

namespace UnityAriadne.Analyzer.Code;

/// <summary>
/// Builds the Addressables key index (address or label → asset GUIDs) from every
/// AddressableAssetGroup .asset in the project, for resolving literal
/// Addressables.Load* keys in the code layer (Addressables backlog).
/// </summary>
/// <remarks>
/// Group assets are found by content, not by path: any ScriptableObject .asset whose
/// YAML contains an <c>m_SerializeEntries</c> list of <c>m_GUID</c>/<c>m_Address</c>
/// mappings (the shape confirmed against real Unity 6000.0 + Addressables 2.3.16
/// output in fixtures/Addressables). A cheap text sniff skips the full YAML parse for
/// the overwhelming majority of ScriptableObjects.
/// </remarks>
public static class AddressableGroupScanner
{
    public static Dictionary<string, List<string>> BuildKeyIndex(
        string projectRoot, IEnumerable<AssetEntry> assets)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            if (asset.AssetType != "ScriptableObject"
                || !asset.Path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullPath = Path.Combine(projectRoot, asset.Path);
            if (!File.Exists(fullPath)
                || !UnityYamlReader.IsUnityYamlFile(fullPath)
                || !File.ReadAllText(fullPath).Contains("m_SerializeEntries", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var document in UnityYamlReader.ReadFile(fullPath))
            {
                if (document.Properties is { } properties
                    && Find(properties, "m_SerializeEntries") is YamlSequenceNode entries)
                {
                    AddEntries(entries, index);
                }
            }
        }

        return index;
    }

    private static void AddEntries(YamlSequenceNode entries, Dictionary<string, List<string>> index)
    {
        foreach (var item in entries.Children.OfType<YamlMappingNode>())
        {
            if (FindScalar(item, "m_GUID") is not { Length: 32 } guid
                || FindScalar(item, "m_Address") is not { } address)
            {
                continue;
            }

            Add(index, address, guid);
            if (Find(item, "m_SerializedLabels") is YamlSequenceNode labels)
            {
                foreach (var label in labels.Children.OfType<YamlScalarNode>())
                {
                    if (label.Value is { Length: > 0 } value)
                    {
                        Add(index, value, guid);
                    }
                }
            }
        }
    }

    private static void Add(Dictionary<string, List<string>> index, string key, string guid)
    {
        if (!index.TryGetValue(key, out var guids))
        {
            index[key] = guids = [];
        }

        if (!guids.Contains(guid))
        {
            guids.Add(guid);
        }
    }

    private static YamlNode? Find(YamlMappingNode mapping, string key)
    {
        foreach (var (keyNode, value) in mapping.Children)
        {
            if (keyNode is YamlScalarNode scalar && scalar.Value == key)
            {
                return value;
            }
        }

        return null;
    }

    private static string? FindScalar(YamlMappingNode mapping, string key) =>
        (Find(mapping, key) as YamlScalarNode)?.Value;
}
