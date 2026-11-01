using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace UnityAriadne.Analyzer.Yaml;

/// <summary>
/// Assigns a <see cref="RefKind"/> to each extracted reference (Phase 1c).
/// <see cref="RefKind.MetaExternal"/> and <see cref="RefKind.BuildSettings"/> come from
/// non-YAML sources and are assigned in Phase 1d, not here.
/// </summary>
public static partial class RefKindClassifier
{
    private const int PrefabInstanceClassId = 1001;

    // Exact paths look like `m_Modification.m_Modifications[3].target`; CLAUDE.md's
    // `m_Modifications[*].target` is matched by suffix.
    [GeneratedRegex(@"m_Modifications\[\d+\]\.target$")]
    private static partial Regex ModificationTarget();

    /// <param name="document">The document the reference was extracted from.</param>
    /// <param name="reference">The reference to classify.</param>
    /// <param name="isPrefabAsset">
    /// True when the containing file is a .prefab. Needed to tell a variant's root
    /// PrefabInstance (variant → base, <see cref="RefKind.SourcePrefab"/>) from a
    /// scene-root PrefabInstance (scene → nested prefab, <see cref="RefKind.PrefabInstance"/>) —
    /// both have <c>m_TransformParent: {fileID: 0}</c>.
    /// </param>
    public static string Classify(UnityYamlDocument document, UnityRef reference, bool isPrefabAsset)
    {
        // JSON-embedded refs (Phase 1g) and Addressables' bare-GUID shapes (backlog)
        // are marked at extraction; the property-path rules below only describe
        // Unity's YAML mapping shape.
        if (reference.FromEmbeddedJson)
        {
            return RefKind.EmbeddedJson;
        }

        if (reference.FromAddressableEntry)
        {
            return RefKind.AddressableEntry;
        }

        if (reference.FromAssetReference)
        {
            return RefKind.AssetReference;
        }

        switch (reference.PropertyPath)
        {
            case "m_Script":
                return RefKind.UsesScript;

            case "m_CorrespondingSourceObject":
                return RefKind.CorrespondingSource;

            // Pre-2018.3 serialization; never a variant/nesting distinction to make.
            case "m_ParentPrefab":
                return RefKind.SourcePrefab;

            case "m_SourcePrefab":
                return isPrefabAsset
                       && document.ClassId == PrefabInstanceClassId
                       && IsFileRootInstance(document)
                    ? RefKind.SourcePrefab
                    : RefKind.PrefabInstance;

            case var path when ModificationTarget().IsMatch(path):
                return RefKind.PrefabModification;

            default:
                return RefKind.Generic;
        }
    }

    /// <summary>
    /// True when the PrefabInstance sits at the file root (<c>m_Modification.m_TransformParent</c>
    /// is the null reference). Inside a .prefab that makes the file a prefab variant.
    /// </summary>
    private static bool IsFileRootInstance(UnityYamlDocument document) =>
        document.Properties is { } properties
        && FindMapping(properties, "m_Modification") is { } modification
        && FindMapping(modification, "m_TransformParent") is { } parent
        && FindScalar(parent, "fileID") == "0";

    private static YamlMappingNode? FindMapping(YamlMappingNode mapping, string key) =>
        Find(mapping, key) as YamlMappingNode;

    private static string? FindScalar(YamlMappingNode mapping, string key) =>
        (Find(mapping, key) as YamlScalarNode)?.Value;

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
}
