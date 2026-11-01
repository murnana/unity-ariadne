using UnityAriadne.Analyzer.Yaml;
using YamlDotNet.RepresentationModel;

namespace UnityAriadne.Analyzer.Settings;

/// <summary>One reference found in a ProjectSettings/*.asset file (Phase 1d).</summary>
/// <param name="SettingsPath">Path relative to the project root, e.g. <c>ProjectSettings/GraphicsSettings.asset</c>.</param>
/// <param name="FromFileId">fileID of the document the reference was found in (1e: objects row key).</param>
/// <param name="FromClassId">Unity class ID of that document.</param>
public sealed record ProjectSettingsRef(
    string SettingsPath, long FromFileId, int FromClassId, UnityRef Ref, string Kind);

/// <summary>
/// Extracts references from ProjectSettings/*.asset (Phase 1d). These are ordinary
/// Unity YAML files (EditorBuildSettings' build scenes, GraphicsSettings' always-included
/// shaders and render pipelines, QualitySettings' per-level pipeline assets, ...), so the
/// Phase 1b reader/extractor is reused. The one special case is EditorBuildSettings'
/// scene list, whose entries are <c>{enabled, path, guid}</c> with a plain-scalar guid —
/// not the reference mapping shape — and need dedicated extraction.
/// </summary>
public static class ProjectSettingsScanner
{
    /// <summary>
    /// The files <see cref="Scan"/> reads, as full paths in scan order — shared with
    /// Phase 4b change detection so both always agree on the input set.
    /// </summary>
    public static IReadOnlyList<string> ListSettingsFiles(string projectRoot)
    {
        var settingsDir = Path.Combine(projectRoot, "ProjectSettings");
        if (!Directory.Exists(settingsDir))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(settingsDir, "*.asset")
            .Where(UnityYamlReader.IsUnityYamlFile)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<ProjectSettingsRef> Scan(string projectRoot)
    {
        var results = new List<ProjectSettingsRef>();

        foreach (var file in ListSettingsFiles(projectRoot))
        {
            var settingsPath = Path.GetRelativePath(projectRoot, file).Replace('\\', '/');

            // Everything EditorBuildSettings references (scenes, config objects) is
            // there because of build configuration — the whole file is build_settings.
            var isBuildSettings = Path.GetFileName(file) == "EditorBuildSettings.asset";

            foreach (var document in UnityYamlReader.ReadFile(file))
            {
                foreach (var reference in ReferenceExtractor.Extract(document))
                {
                    if (reference.IsNull)
                    {
                        continue;
                    }

                    var kind = isBuildSettings
                        ? RefKind.BuildSettings
                        : RefKindClassifier.Classify(document, reference, isPrefabAsset: false);
                    results.Add(new ProjectSettingsRef(
                        settingsPath, document.FileId, document.ClassId, reference, kind));
                }

                if (isBuildSettings)
                {
                    results.AddRange(ExtractSceneRefs(document, settingsPath));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Turns each <c>m_Scenes</c> entry with a guid into a reference to the scene asset.
    /// Disabled scenes are still references. The scene asset has no target fileID, so 0
    /// is used (with a guid present that is not the null reference).
    /// </summary>
    private static IEnumerable<ProjectSettingsRef> ExtractSceneRefs(UnityYamlDocument document, string settingsPath)
    {
        if (document.Properties is not { } properties
            || Find(properties, "m_Scenes") is not YamlSequenceNode scenes)
        {
            yield break;
        }

        for (var i = 0; i < scenes.Children.Count; i++)
        {
            if (scenes.Children[i] is YamlMappingNode entry
                && Find(entry, "guid") is YamlScalarNode { Value: { Length: > 0 } guid })
            {
                yield return new ProjectSettingsRef(
                    settingsPath,
                    document.FileId,
                    document.ClassId,
                    new UnityRef { FileId = 0, Guid = guid, PropertyPath = $"m_Scenes[{i}]" },
                    RefKind.BuildSettings);
            }
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
}
