using UnityAriadne.Analyzer.Scanning;
using UnityAriadne.Analyzer.Storage;
using UnityAriadne.Graph;

namespace UnityAriadne.Tests;

/// <summary>
/// One full fixture analysis into an in-memory database, shared by every test class in
/// the "fixture-graph" collection (GraphBuilderTests, MemoryJsonlExporterTests).
/// </summary>
public sealed class FixtureGraphFixture : IDisposable
{
    public GraphDatabase Db { get; }

    public AnalysisSummary Summary { get; } = null!;

    public FixtureGraphFixture()
    {
        Db = GraphDatabase.CreateInMemory();
        if (!Fixtures.IsAvailable)
        {
            return; // every test in the collection is [FixtureFact] and skipped
        }

        Summary = GraphBuilder.Build(
            Fixtures.SampleProject, UnityProjectScanner.Scan(Fixtures.SampleProject), Db);
    }

    public void Dispose() => Db.Dispose();
}

[CollectionDefinition("fixture-graph")]
public sealed class FixtureGraphCollection : ICollectionFixture<FixtureGraphFixture>;

[Collection("fixture-graph")]
public class GraphBuilderTests(FixtureGraphFixture fixture)
{
    private GraphDatabase Db => fixture.Db;

    private AnalysisSummary Summary => fixture.Summary;

    /// <summary>
    /// The 1c YAML kind census (RefKindClassifierTests) plus the 1d meta census
    /// (753 meta_external), now read back from the database. Scoped to source='Assets'
    /// so the result is identical with and without a local PackageCache. Code-layer
    /// kinds (Phase 2) are excluded here because their resolution DOES depend on
    /// PackageCache presence — see CodeLayerFixtureTests for that census.
    /// </summary>
    [FixtureFact]
    public void AssetsRefCensusMatchesGroundTruth()
    {
        var census = GraphDatabaseTests.QueryStrings(Db,
            $"""
            SELECT r.ref_kind || ': ' || COUNT(*)
            FROM refs r
            JOIN objects o ON o.id = r.from_object_id
            JOIN assets a ON a.id = o.asset_id
            WHERE a.source = 'Assets' AND r.ref_kind NOT IN {CodeLayerFixtureTests.CodeKindsSql}
            GROUP BY r.ref_kind
            ORDER BY r.ref_kind
            """);

        Assert.Equal(
            [
                "asset_reference: 6", // URP APV baking set → its baked .bytes assets (bare m_AssetGUID)
                "corresponding_source: 1684",
                "embedded_json: 132", // 26 from .vfx JSON-in-YAML + 106 from .shadergraph/.shadersubgraph (1g)
                "generic: 23142",
                "meta_external: 753",
                "prefab_instance: 1451",
                "prefab_modification: 28820",
                "source_prefab: 64",
                "uses_script: 3932",
            ],
            census);
    }

    [FixtureFact]
    public void AssetsObjectCensusMatchesGroundTruth()
    {
        // Meta pseudo-objects are (file_id 0, class_id 0); real documents never have fileID 0.
        // MonoScript objects (Phase 2: file_id 11500000, class_id 115) are excluded too —
        // how many scripts get one depends on PackageCache presence.
        Assert.Equal(9148, CountAssetsObjects(
            "(o.class_id <> 0 OR o.file_id <> 0) AND NOT (o.file_id = 11500000 AND o.class_id = 115)"));
        Assert.Equal(1684, CountAssetsObjects("o.is_stripped = 1"));
        Assert.Equal(75, CountAssetsObjects("o.file_id < 0"));

        // 1d census: 753 meta refs in 400 .meta files under Assets/ → 400 pseudo-objects.
        // Still 400 after 1g: every Assets/ shadergraph with JSON refs shares the pseudo-
        // object its ScriptedImporter meta already created.
        Assert.Equal(400, CountAssetsObjects("o.class_id = 0 AND o.file_id = 0"));
    }

    [FixtureFact]
    public void ProjectSettingsAreStoredAsPathKeyedAssets()
    {
        Assert.Equal(
            ["ProjectSettings|ProjectSettings"],
            GraphDatabaseTests.QueryStrings(Db,
                "SELECT DISTINCT asset_type || '|' || source FROM assets WHERE source = 'ProjectSettings'"));
        Assert.Equal(10, GraphDatabaseTests.QueryLong(Db,
            "SELECT COUNT(*) FROM assets WHERE source = 'ProjectSettings' AND guid = path"));

        // 1d settings census, read back through the refs table.
        Assert.Equal(
            ["build_settings: 3", "generic: 31", "uses_script: 4"],
            GraphDatabaseTests.QueryStrings(Db,
                """
                SELECT r.ref_kind || ': ' || COUNT(*)
                FROM refs r
                JOIN objects o ON o.id = r.from_object_id
                JOIN assets a ON a.id = o.asset_id
                WHERE a.source = 'ProjectSettings'
                GROUP BY r.ref_kind
                ORDER BY r.ref_kind
                """));

        // The build-scene list surfaces as asset-level edges to the scene assets.
        Assert.Equal(
            ["a2bcfd0088dab57498427eb47330d215"],
            GraphDatabaseTests.QueryStrings(Db,
                """
                SELECT to_guid FROM asset_edges
                WHERE from_guid = 'ProjectSettings/EditorBuildSettings.asset'
                  AND ref_kind = 'build_settings'
                  AND to_guid = 'a2bcfd0088dab57498427eb47330d215'
                """));
    }

    /// <summary>
    /// Phase 1e decision: Unity's built-in GUIDs resolve to virtual paths with
    /// source='BuiltIn' instead of showing up in the unresolved report. Both are
    /// referenced from Assets/ProjectSettings, so this holds without a PackageCache.
    /// </summary>
    [FixtureFact]
    public void BuiltInGuidsResolveToVirtualPaths()
    {
        Assert.Equal(
            [
                "0000000000000000e000000000000000|Library/unity editor resources",
                "0000000000000000f000000000000000|Resources/unity_builtin_extra",
            ],
            GraphDatabaseTests.QueryStrings(Db,
                "SELECT guid || '|' || path FROM assets WHERE source = 'BuiltIn' ORDER BY guid"));
        Assert.Equal(0, GraphDatabaseTests.QueryLong(Db,
            "SELECT COUNT(*) FROM unresolved_guids WHERE guid LIKE '0000000000000000%'"));
    }

    /// <summary>
    /// The fixture's dangling references (left over from trimming, ground truth per
    /// CLAUDE.md) land in the unresolved report. This guid is referenced by 17 bush/tree
    /// prefabs under Assets/ and resolved nowhere, PackageCache or not.
    /// </summary>
    [FixtureFact]
    public void KnownDanglingGuidIsReportedUnresolved()
    {
        const string DANGLING_GUID = "299147e0b236560478d85010e9e1c387";

        Assert.Equal(
            [DANGLING_GUID + "|Unknown|Unresolved"],
            GraphDatabaseTests.QueryStrings(Db,
                $"SELECT guid || '|' || asset_type || '|' || source FROM assets WHERE guid = '{DANGLING_GUID}' AND path IS NULL"));

        var info = Assert.Single(Db.GetUnresolvedGuids(), i => i.Guid == DANGLING_GUID);
        Assert.Equal(31, info.RefCount);
        Assert.Equal(17, info.ReferrerCount);
        Assert.All(info.SampleReferrers,
            path => Assert.StartsWith("Assets/Art/Prefabs/Foliage/", path, StringComparison.Ordinal));
    }

    /// <summary>The console summary and the database always describe the same graph.</summary>
    [FixtureFact]
    public void SummaryCountersMatchDatabase()
    {
        Assert.Equal(Summary.TotalRefCount,
            GraphDatabaseTests.QueryLong(Db, "SELECT COUNT(*) FROM refs"));
        // Same-file refs recorded by the summary are YAML-only, so exclude the null-guid
        // refs that meta files or ProjectSettings may contribute.
        Assert.Equal(Summary.SameFileRefCount,
            GraphDatabaseTests.QueryLong(Db,
                """
                SELECT COUNT(*) FROM refs r
                JOIN objects o ON o.id = r.from_object_id
                JOIN assets a ON a.id = o.asset_id
                WHERE r.to_guid IS NULL
                  AND r.ref_kind <> 'meta_external'
                  AND a.source <> 'ProjectSettings'
                """));
        Assert.Equal(Summary.UnresolvedGuidCount,
            GraphDatabaseTests.QueryLong(Db, "SELECT COUNT(*) FROM unresolved_guids"));
        Assert.Equal(Summary.ObjectCount + Summary.PseudoObjectCount + Summary.ScriptObjectCount,
            GraphDatabaseTests.QueryLong(Db,
                """
                SELECT COUNT(*) FROM objects o
                JOIN assets a ON a.id = o.asset_id
                WHERE a.source <> 'ProjectSettings'
                """));
    }

    /// <summary>
    /// Exact fixture-wide numbers, only meaningful when the local machine has opened the
    /// fixture in Unity (package GUIDs resolve). Never runs in CI.
    /// </summary>
    [FixtureFact]
    public void WithPackageCacheUnresolvedCensusMatchesGroundTruth()
    {
        if (!Fixtures.HasPackageCache)
        {
            return;
        }

        // 83 = the 76 counted before 1g + 7 dangling guids referenced only from JSON
        // (.vfx / .shadergraph) — invisible until embedded_json extraction.
        Assert.Equal(83, GraphDatabaseTests.QueryLong(Db, "SELECT COUNT(*) FROM unresolved_guids"));

        // The subset actually referenced from Assets/ (36 before 1g).
        Assert.Equal(40, GraphDatabaseTests.QueryLong(Db,
            """
            SELECT COUNT(DISTINCT r.to_guid)
            FROM refs r
            JOIN objects o ON o.id = r.from_object_id
            JOIN assets a ON a.id = o.asset_id
            WHERE a.source = 'Assets'
              AND r.to_guid IN (SELECT guid FROM unresolved_guids)
            """));
    }

    private long CountAssetsObjects(string condition) =>
        GraphDatabaseTests.QueryLong(Db,
            $"""
             SELECT COUNT(*) FROM objects o
             JOIN assets a ON a.id = o.asset_id
             WHERE a.source = 'Assets' AND ({condition})
             """);
}
