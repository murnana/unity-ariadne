using UnityAriadne.Analyzer.Code;
using UnityAriadne.Analyzer.Json;
using UnityAriadne.Analyzer.Scanning;
using UnityAriadne.Analyzer.Settings;
using UnityAriadne.Analyzer.Yaml;
using UnityAriadne.Graph;

namespace UnityAriadne.Analyzer.Storage;

/// <summary>
/// Writes one full analysis pass — GUID dictionary (1a), YAML objects and refs (1b/1c),
/// .meta and ProjectSettings refs (1d), JSON asset refs (1g) — into a
/// <see cref="GraphDatabase"/> in a single transaction (Phase 1e).
/// </summary>
/// <remarks>
/// Phase 1e decisions:
/// <list type="bullet">
/// <item>.meta refs and JSON-file refs (Phase 1g: .shadergraph/.shadersubgraph) hang off
/// a pseudo-object with <c>file_id = 0</c>, <c>class_id = 0</c> (real document anchors
/// are never 0 — that is Unity's null fileID), shared per asset and created only when
/// the asset actually has such refs.</item>
/// <item>ProjectSettings files have no GUID, so their assets row uses the project-relative
/// path (e.g. <c>ProjectSettings/EditorBuildSettings.asset</c>) as the guid value, with
/// asset_type and source both 'ProjectSettings'. Their objects rows are created on demand
/// from the documents that carry refs.</item>
/// <item>Unity's built-in GUIDs (<c>…d000…</c>/<c>…e000…</c>/<c>…f000…</c>) resolve to
/// virtual paths with source='BuiltIn' instead of polluting the unresolved report.</item>
/// <item>Any other referenced GUID without a .meta becomes source='Unresolved',
/// asset_type='Unknown', path NULL (feeds the unresolved_guids view).</item>
/// </list>
/// </remarks>
public static class GraphBuilder
{
    public const long MetaPseudoFileId = 0;

    public const string ProjectSettingsSource = "ProjectSettings";

    // Virtual assets Unity ships with; no .meta exists for them anywhere on disk.
    private static readonly IReadOnlyDictionary<string, string> BuiltInPaths =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["0000000000000000d000000000000000"] = "Library/unity default resources",
            ["0000000000000000e000000000000000"] = "Library/unity editor resources",
            ["0000000000000000f000000000000000"] = "Resources/unity_builtin_extra",
        };

    public static AnalysisSummary Build(string projectRoot, GuidDictionary dictionary, GraphDatabase db)
    {
        var summary = new AnalysisSummary();
        using var writer = db.CreateWriter();
        var hashes = ComputeHashes(projectRoot, dictionary);

        var assetIds = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var entry in dictionary.Entries)
        {
            assetIds[entry.Guid] = writer.AddAsset(
                entry.Guid, entry.Path, entry.AssetType, entry.Source, hashes[entry.Guid]);
        }

        foreach (var entry in dictionary.Entries)
        {
            WriteAssetContent(projectRoot, entry, writer, assetIds, summary);
        }

        WriteProjectSettings(projectRoot, writer, assetIds, summary);
        WriteCodeRefs(projectRoot, dictionary, writer, assetIds, summary);

        writer.Commit();
        return summary;
    }

    /// <summary>Phase 4b: change-detection hashes for every dictionary entry, in parallel
    /// (hashing is pure I/O + SHA-256; order does not matter).</summary>
    internal static Dictionary<string, string> ComputeHashes(string projectRoot, GuidDictionary dictionary) =>
        dictionary.Entries
            .AsParallel()
            .Select(entry => (entry.Guid, Hash: ContentHasher.HashAsset(projectRoot, entry)))
            .ToDictionary(pair => pair.Guid, pair => pair.Hash, StringComparer.Ordinal);

    /// <summary>
    /// Everything derived from one asset's own files: YAML documents and refs (1b/1c),
    /// JSON-file refs (1g) and .meta refs (1d). Code-layer refs are not per-asset —
    /// see <see cref="WriteCodeRefs"/>.
    /// </summary>
    internal static void WriteAssetContent(
        string projectRoot, AssetEntry entry, GraphWriter writer,
        Dictionary<string, long> assetIds, AnalysisSummary summary)
    {
        WriteYamlDocuments(projectRoot, entry, writer, assetIds, summary);

        // .meta refs and JSON-file refs share one per-asset pseudo-object
        // (a shadergraph's ScriptedImporter meta usually has refs too).
        long? pseudoObjectId = null;
        WriteJsonAssetRefs(projectRoot, entry, writer, assetIds, summary, ref pseudoObjectId);
        WriteMetaRefs(projectRoot, entry, writer, assetIds, summary, ref pseudoObjectId);
    }

    /// <summary>
    /// Phase 2: code-layer refs from .cs sources hang off a per-script MonoScript object
    /// (file_id 11500000, class_id 115 — the object uses_script YAML refs point at),
    /// created only for scripts that actually have resolved refs. Type/Resources.Load
    /// targets come from the GUID dictionary; addressables_load targets come from group
    /// entries and may be dangling (see below).
    /// </summary>
    internal static void WriteCodeRefs(
        string projectRoot, GuidDictionary dictionary, GraphWriter writer,
        Dictionary<string, long> assetIds, AnalysisSummary summary)
    {
        var result = CodeScanner.Scan(projectRoot, dictionary);
        summary.ScriptFileCount = result.ScriptFileCount;
        summary.DeclaredTypeCount = result.DeclaredTypeCount;
        summary.AmbiguousTypeRefCount = result.AmbiguousTypeRefCount;
        summary.SameFileCodeRefCount = result.SameFileRefCount;
        summary.UnresolvedResourceLoadCount = result.UnresolvedResourceLoadCount;
        summary.NonLiteralResourceLoadCount = result.NonLiteralResourceLoadCount;
        summary.UnresolvedAddressablesLoadCount = result.UnresolvedAddressablesLoadCount;
        summary.NonLiteralAddressablesLoadCount = result.NonLiteralAddressablesLoadCount;

        foreach (var script in result.Scripts)
        {
            var objectId = writer.AddObject(
                assetIds[script.Guid], CodeScanner.MonoScriptFileId, CodeScanner.MonoScriptClassId,
                script.ObjectName, isStripped: false);
            summary.ScriptObjectCount++;

            foreach (var reference in script.Refs)
            {
                // Only addressables_load can target a GUID outside the dictionary: a
                // group entry whose asset was deleted resolves a literal key to a
                // dangling GUID (the asset layer created the same Unresolved row from
                // the entry itself, so this is a no-op on a full build).
                EnsureTargetAsset(reference.ToGuid, writer, assetIds, summary);
                writer.AddRef(objectId, reference.ToGuid, reference.ToFileId, reference.PropertyPath, reference.Kind);
                summary.CountCodeKind(reference.Kind);
                summary.CodeRefCount++;
            }
        }
    }

    private static void WriteYamlDocuments(
        string projectRoot, AssetEntry entry, GraphWriter writer,
        Dictionary<string, long> assetIds, AnalysisSummary summary)
    {
        var fullPath = Path.Combine(projectRoot, entry.Path);
        if (entry.AssetType == "Folder" || !File.Exists(fullPath) || !UnityYamlReader.IsUnityYamlFile(fullPath))
        {
            return;
        }

        summary.YamlFileCount++;
        var assetId = assetIds[entry.Guid];
        var isPrefabAsset = entry.AssetType == "Prefab";
        var objectIds = new Dictionary<long, long>();

        foreach (var document in UnityYamlReader.ReadFile(fullPath))
        {
            // Duplicate anchors within one file would violate UNIQUE(asset_id, file_id);
            // Unity never writes them, but keep first-wins for robustness.
            if (!objectIds.TryGetValue(document.FileId, out var objectId))
            {
                objectId = writer.AddObject(assetId, document.FileId, document.ClassId, document.Name, document.IsStripped);
                objectIds[document.FileId] = objectId;
                summary.ObjectCount++;
            }

            foreach (var reference in ReferenceExtractor.Extract(document))
            {
                if (reference.IsNull)
                {
                    summary.NullRefCount++;
                    continue;
                }

                var kind = RefKindClassifier.Classify(document, reference, isPrefabAsset);
                summary.CountYamlKind(kind);
                if (reference.Guid is null)
                {
                    summary.SameFileRefCount++;
                }
                else
                {
                    summary.ExternalRefCount++;
                    EnsureTargetAsset(reference.Guid, writer, assetIds, summary);
                }

                writer.AddRef(objectId, reference.Guid, reference.FileId, reference.PropertyPath, kind);
            }
        }
    }

    /// <summary>
    /// JSON asset files (Phase 1g: .shadergraph/.shadersubgraph) have no YAML documents,
    /// so their refs hang off the per-asset pseudo-object, like .meta refs.
    /// </summary>
    private static void WriteJsonAssetRefs(
        string projectRoot, AssetEntry entry, GraphWriter writer,
        Dictionary<string, long> assetIds, AnalysisSummary summary, ref long? pseudoObjectId)
    {
        if (!entry.Path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase)
            && !entry.Path.EndsWith(".shadersubgraph", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fullPath = Path.Combine(projectRoot, entry.Path);
        if (entry.AssetType == "Folder" || !File.Exists(fullPath))
        {
            return;
        }

        var hadRefs = false;
        foreach (var reference in JsonRefScanner.Scan(File.ReadAllText(fullPath)))
        {
            hadRefs = true;
            pseudoObjectId ??= AddPseudoObject(entry, writer, assetIds, summary);
            EnsureTargetAsset(reference.Guid!, writer, assetIds, summary);
            writer.AddRef(pseudoObjectId.Value, reference.Guid, reference.FileId, reference.PropertyPath, RefKind.EmbeddedJson);
            summary.JsonRefCount++;
        }

        if (hadRefs)
        {
            summary.JsonFilesWithRefs++;
        }
    }

    private static void WriteMetaRefs(
        string projectRoot, AssetEntry entry, GraphWriter writer,
        Dictionary<string, long> assetIds, AnalysisSummary summary, ref long? pseudoObjectId)
    {
        var metaPath = Path.Combine(projectRoot, entry.Path + ".meta");
        if (!File.Exists(metaPath))
        {
            return;
        }

        var metaRefs = MetaReferenceExtractor.ExtractFile(metaPath, out var usedRegexFallback);
        if (usedRegexFallback)
        {
            summary.MetaRegexFallbackCount++;
        }

        var hadRefs = false;
        foreach (var reference in metaRefs)
        {
            if (reference.IsNull)
            {
                continue;
            }

            hadRefs = true;
            pseudoObjectId ??= AddPseudoObject(entry, writer, assetIds, summary);
            if (reference.Guid is not null)
            {
                EnsureTargetAsset(reference.Guid, writer, assetIds, summary);
            }

            writer.AddRef(pseudoObjectId.Value, reference.Guid, reference.FileId, reference.PropertyPath, RefKind.MetaExternal);
            summary.MetaRefCount++;
        }

        if (hadRefs)
        {
            summary.MetaFilesWithRefs++;
        }
    }

    private static long AddPseudoObject(
        AssetEntry entry, GraphWriter writer, Dictionary<string, long> assetIds, AnalysisSummary summary)
    {
        summary.PseudoObjectCount++;
        return writer.AddObject(
            assetIds[entry.Guid], MetaPseudoFileId, classId: 0, name: null, isStripped: false);
    }

    internal static void WriteProjectSettings(
        string projectRoot, GraphWriter writer,
        Dictionary<string, long> assetIds, AnalysisSummary summary)
    {
        var settingsAssetIds = new Dictionary<string, long>(StringComparer.Ordinal);
        var settingsObjectIds = new Dictionary<(string Path, long FileId), long>();

        // Every scanned file is recorded for change detection, including ones with no
        // refs (those never get an assets row).
        foreach (var file in ProjectSettingsScanner.ListSettingsFiles(projectRoot))
        {
            writer.SetSettingsFile(
                Path.GetRelativePath(projectRoot, file).Replace('\\', '/'),
                ContentHasher.HashFile(file));
        }

        foreach (var settingsRef in ProjectSettingsScanner.Scan(projectRoot))
        {
            if (!settingsAssetIds.TryGetValue(settingsRef.SettingsPath, out var assetId))
            {
                assetId = writer.AddAsset(
                    settingsRef.SettingsPath, settingsRef.SettingsPath,
                    ProjectSettingsSource, ProjectSettingsSource);
                settingsAssetIds[settingsRef.SettingsPath] = assetId;
            }

            var objectKey = (settingsRef.SettingsPath, settingsRef.FromFileId);
            if (!settingsObjectIds.TryGetValue(objectKey, out var objectId))
            {
                objectId = writer.AddObject(
                    assetId, settingsRef.FromFileId, settingsRef.FromClassId, name: null, isStripped: false);
                settingsObjectIds[objectKey] = objectId;
            }

            if (settingsRef.Ref.Guid is not null)
            {
                EnsureTargetAsset(settingsRef.Ref.Guid, writer, assetIds, summary);
            }

            writer.AddRef(
                objectId, settingsRef.Ref.Guid, settingsRef.Ref.FileId,
                settingsRef.Ref.PropertyPath, settingsRef.Kind);
            summary.CountSettingsKind(settingsRef.Kind);
            summary.SettingsRefCount++;
        }

        summary.SettingsFileCount = settingsAssetIds.Count;
    }

    /// <summary>
    /// Registers a referenced GUID the scan did not find: a built-in Unity resource or a
    /// genuinely unresolved (dangling) GUID.
    /// </summary>
    internal static void EnsureTargetAsset(
        string guid, GraphWriter writer, Dictionary<string, long> assetIds, AnalysisSummary summary)
    {
        if (assetIds.ContainsKey(guid))
        {
            return;
        }

        if (BuiltInPaths.TryGetValue(guid, out var builtInPath))
        {
            assetIds[guid] = writer.AddAsset(guid, builtInPath, "BuiltinResource", "BuiltIn");
            summary.BuiltInGuidCount++;
        }
        else
        {
            assetIds[guid] = writer.AddAsset(guid, path: null, "Unknown", "Unresolved");
            summary.UnresolvedGuidCount++;
        }
    }
}
