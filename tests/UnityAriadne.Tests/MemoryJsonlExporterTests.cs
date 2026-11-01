using System.Text.Json;
using UnityAriadne.Graph;

namespace UnityAriadne.Tests;

public class MemoryJsonlExporterTests
{
    /// <summary>
    /// One graph exercising every export rule: ref_count aggregation, guid/source/count
    /// observations, path-keyed ProjectSettings (no guid observation), a dangling GUID,
    /// a PackageCache asset kept as target-only, another skipped entirely, and an
    /// edge-less folder dropped. Exact-bytes golden test.
    /// </summary>
    [Fact]
    public void DefaultExportMatchesGolden()
    {
        using var db = CreateSyntheticGraph();

        var (lines, summary) = Export(db, new ExportOptions());

        Assert.Equal(
            [
                """{"type":"entity","name":"Assets/P.prefab","entityType":"Prefab","observations":["guid: bbbb","source: Assets","outgoing: 2 refs to 2 assets","incoming: 2 refs from 1 assets"]}""",
                """{"type":"entity","name":"Assets/S.unity","entityType":"Scene","observations":["guid: aaaa","source: Assets","outgoing: 2 refs to 1 assets","incoming: 1 refs from 1 assets"]}""",
                """{"type":"entity","name":"Library/PackageCache/com.x/Runtime/X.cs","entityType":"Script","observations":["guid: cccc","source: PackageCache","incoming: 1 refs from 1 assets"]}""",
                """{"type":"entity","name":"ProjectSettings/EditorBuildSettings.asset","entityType":"ProjectSettings","observations":["source: ProjectSettings","outgoing: 1 refs to 1 assets"]}""",
                """{"type":"entity","name":"dead","entityType":"Unknown","observations":["source: Unresolved","dangling reference: no asset with this GUID exists in the project","incoming: 1 refs from 1 assets"]}""",
                """{"type":"relation","from":"Assets/P.prefab","to":"Library/PackageCache/com.x/Runtime/X.cs","relationType":"uses_script","ref_count":1}""",
                """{"type":"relation","from":"Assets/P.prefab","to":"dead","relationType":"generic","ref_count":1}""",
                """{"type":"relation","from":"Assets/S.unity","to":"Assets/P.prefab","relationType":"prefab_instance","ref_count":2}""",
                """{"type":"relation","from":"ProjectSettings/EditorBuildSettings.asset","to":"Assets/S.unity","relationType":"build_settings","ref_count":1}""",
            ],
            lines);
        Assert.Equal(new ExportSummary(5, 4, SkippedPackageCacheAssetCount: 1), summary);
    }

    [Fact]
    public void IncludePackageCacheExportsPackageNodesAndTheirEdges()
    {
        using var db = CreateSyntheticGraph();

        var (lines, summary) = Export(db, new ExportOptions { IncludePackageCache = true });

        Assert.Contains(
            """{"type":"entity","name":"Library/PackageCache/com.x/Other.mat","entityType":"Material","observations":["guid: dddd","source: PackageCache","outgoing: 1 refs to 1 assets"]}""",
            lines);
        Assert.Contains(
            """{"type":"relation","from":"Library/PackageCache/com.x/Other.mat","to":"Assets/P.prefab","relationType":"generic","ref_count":1}""",
            lines);
        Assert.Equal(new ExportSummary(6, 5, SkippedPackageCacheAssetCount: 0), summary);
    }

    [Fact]
    public void FolderIsExportedOnlyWhenConnected()
    {
        using var db = GraphDatabase.CreateInMemory();
        using (var writer = db.CreateWriter())
        {
            var scene = writer.AddAsset("aaaa", "Assets/S.unity", "Scene", "Assets");
            writer.AddAsset("f001", "Assets/Referenced", "Folder", "Assets");
            writer.AddAsset("f002", "Assets/Orphan", "Folder", "Assets");
            var sceneObject = writer.AddObject(scene, 1, 1, null, false);
            writer.AddRef(sceneObject, "f001", 102900000, "m_Folder", "generic");
            writer.Commit();
        }

        var (lines, _) = Export(db, new ExportOptions());

        Assert.Contains(lines, l => l.Contains("\"Assets/Referenced\""));
        Assert.DoesNotContain(lines, l => l.Contains("\"Assets/Orphan\""));
    }

    /// <summary>Phase 1c decision: corresponding_source stays in SQLite but not in the jsonl.</summary>
    [Fact]
    public void CorrespondingSourceEdgesAreExcluded()
    {
        using var db = CreateSyntheticGraph();

        var (lines, _) = Export(db, new ExportOptions { IncludePackageCache = true });

        Assert.DoesNotContain(lines, l => l.Contains("corresponding_source"));
    }

    /// <summary>
    /// aaaa (scene) → bbbb (prefab) ×2 + one corresponding_source; bbbb → cccc
    /// (PackageCache script) and dead (unresolved); dddd (PackageCache material) → bbbb;
    /// EditorBuildSettings → aaaa; ffff is an edge-less folder.
    /// </summary>
    private static GraphDatabase CreateSyntheticGraph()
    {
        var db = GraphDatabase.CreateInMemory();
        using var writer = db.CreateWriter();

        var scene = writer.AddAsset("aaaa", "Assets/S.unity", "Scene", "Assets");
        var prefab = writer.AddAsset("bbbb", "Assets/P.prefab", "Prefab", "Assets");
        var packageScript = writer.AddAsset("cccc", "Library/PackageCache/com.x/Runtime/X.cs", "Script", "PackageCache");
        var packageMaterial = writer.AddAsset("dddd", "Library/PackageCache/com.x/Other.mat", "Material", "PackageCache");
        writer.AddAsset("ffff", "Assets/Sub", "Folder", "Assets");
        writer.AddAsset("dead", null, "Unknown", "Unresolved");
        var settings = writer.AddAsset(
            "ProjectSettings/EditorBuildSettings.asset", "ProjectSettings/EditorBuildSettings.asset",
            "ProjectSettings", "ProjectSettings");

        var sceneObject = writer.AddObject(scene, 1, 1001, null, false);
        var prefabObject = writer.AddObject(prefab, 100, 114, null, false);
        var packageObject = writer.AddObject(packageMaterial, 2100000, 21, null, false);
        var settingsObject = writer.AddObject(settings, 1, 1045, null, false);

        // Same kind twice → one relation line with ref_count 2.
        writer.AddRef(sceneObject, "bbbb", 100, "m_SourcePrefab", "prefab_instance");
        writer.AddRef(sceneObject, "bbbb", 200, "m_SourcePrefab", "prefab_instance");
        writer.AddRef(sceneObject, "bbbb", 300, "m_CorrespondingSourceObject", "corresponding_source");
        writer.AddRef(prefabObject, "cccc", 11500000, "m_Script", "uses_script");
        writer.AddRef(prefabObject, "dead", 2100000, "m_Materials[0]", "generic");
        writer.AddRef(packageObject, "bbbb", 100, "m_Parent", "generic");
        writer.AddRef(settingsObject, "aaaa", 0, "m_Scenes[0]", "build_settings");
        writer.Commit();

        return db;
    }

    internal static (IReadOnlyList<string> Lines, ExportSummary Summary) Export(GraphDatabase db, ExportOptions options)
    {
        using var output = new StringWriter();
        var summary = MemoryJsonlExporter.Export(db, output, options);
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToList();
        return (lines, summary);
    }
}

[Collection("fixture-graph")]
public class MemoryJsonlExporterFixtureTests(FixtureGraphFixture fixture)
{
    /// <summary>
    /// Structural invariants over the whole fixture export: every line is valid JSON,
    /// entities come first and are sorted, and every relation endpoint has an entity
    /// (the memory server rejects relations to unknown names).
    /// </summary>
    [FixtureFact]
    public void DefaultExportIsSortedJsonlWithClosedRelationEndpoints()
    {
        var (lines, summary) = MemoryJsonlExporterTests.Export(fixture.Db, new ExportOptions());

        var entityNames = new List<string>();
        var relationCount = 0;
        foreach (var line in lines)
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "entity":
                    Assert.Equal(0, relationCount); // entities precede relations
                    entityNames.Add(root.GetProperty("name").GetString()!);
                    break;
                case "relation":
                    relationCount++;
                    Assert.NotEqual("corresponding_source", root.GetProperty("relationType").GetString());
                    // Default export never emits edges originating in PackageCache.
                    Assert.DoesNotMatch("^Library/PackageCache/", root.GetProperty("from").GetString());
                    Assert.Contains(root.GetProperty("from").GetString(), entityNames);
                    Assert.Contains(root.GetProperty("to").GetString(), entityNames);
                    Assert.True(root.GetProperty("ref_count").GetInt64() > 0);
                    break;
                default:
                    Assert.Fail($"unexpected line type in: {line}");
                    break;
            }
        }

        Assert.Equal(entityNames.OrderBy(n => n, StringComparer.Ordinal), entityNames);
        Assert.Equal(entityNames.Count, summary.EntityCount);
        Assert.Equal(relationCount, summary.RelationCount);
    }

    [FixtureFact]
    public void KnownFixtureFactsAppearInExport()
    {
        var (lines, _) = MemoryJsonlExporterTests.Export(fixture.Db, new ExportOptions());

        // The build-scene list (Phase 1d) survives as a build_settings relation.
        Assert.Contains(lines, l =>
            l.Contains("\"from\":\"ProjectSettings/EditorBuildSettings.asset\"")
            && l.Contains("\"to\":\"Assets/Scenes/Loader.unity\"")
            && l.Contains("\"relationType\":\"build_settings\""));

        // The known dangling GUID (CLAUDE.md ground truth) exports as an Unknown entity
        // named by its GUID, flagged as dangling.
        Assert.Contains(lines, l =>
            l.Contains("\"name\":\"299147e0b236560478d85010e9e1c387\"")
            && l.Contains("\"entityType\":\"Unknown\"")
            && l.Contains("dangling reference"));

        // Code-layer edges (Phase 2) survive as relations between script entities.
        Assert.Contains(lines, l =>
            l.Contains("\"from\":\"Assets/Scripts/InputProviders/InputProvider.cs\"")
            && l.Contains("\"to\":\"Assets/Scripts/Interfaces/IInputProvider.cs\"")
            && l.Contains("\"relationType\":\"inherits\""));
    }

    /// <summary>
    /// Exact fixture-wide sizes, PackageCache-dependent — never runs in CI
    /// (same gating as GraphBuilderTests).
    /// </summary>
    [FixtureFact]
    public void WithPackageCacheExportSizesMatchGroundTruth()
    {
        if (!Fixtures.HasPackageCache)
        {
            return;
        }

        var (_, defaults) = MemoryJsonlExporterTests.Export(fixture.Db, new ExportOptions());
        var (_, everything) = MemoryJsonlExporterTests.Export(fixture.Db, new ExportOptions { IncludePackageCache = true });

        // 1273 = the 1272 of Phase 1g + 1 PackageCache script only code refs reach:
        // InputActionAsset.cs via [SerializeField] InputActionAsset fields — an edge the
        // asset layer never sees (it was a Unity-only ImportResultOnlyGaps entry).
        // The backlog kinds (nonserialized_field/custom_editor) add 7 more PackageCache
        // scripts reached only via private fields (Cinemachine/URP/InputSystem types) and
        // lift the aggregated code edges: 3483 → 3508 from the 61 Assets-origin code refs.
        // The Addressables bare-GUID shape adds 6 asset_reference edges (the APV baking
        // set's m_AssetGUID refs to its baked .bytes assets) → 3514.
        Assert.Equal(new ExportSummary(1280, 3514, SkippedPackageCacheAssetCount: 13215), defaults);
        Assert.Equal(16606, everything.RelationCount);
        Assert.Equal(0, everything.SkippedPackageCacheAssetCount);
        Assert.True(everything.EntityCount > defaults.EntityCount);
    }
}
