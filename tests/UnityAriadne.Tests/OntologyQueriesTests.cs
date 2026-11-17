using UnityAriadne.Graph;

namespace UnityAriadne.Tests;

/// <summary>
/// The typed (ontology) queries on a small synthetic graph with a code layer:
///
///   Enemy.cs ◄─inherits── Boss.cs ◄─inherits── FinalBoss.cs
///      ▲                     ▲
///   uses_script           uses_script
///      │                     │
///   Grunt.prefab          Boss.prefab ──generic──► Boss.mat
///      ▲                     ▲
///      └──prefab_instance── Level1.unity ──prefab_instance──┘
///   Unused.prefab (no edges)        Level1.unity ──corresponding_source──► Unused.prefab (ignored)
/// </summary>
public class OntologyQueriesTests : IDisposable
{
    private const string EnemyGuid = "bbbb0000000000000000000000000001";
    private const string BossGuid = "bbbb0000000000000000000000000002";
    private const string FinalBossGuid = "bbbb0000000000000000000000000003";
    private const string GruntPrefabGuid = "bbbb0000000000000000000000000004";
    private const string BossPrefabGuid = "bbbb0000000000000000000000000005";
    private const string SceneGuid = "bbbb0000000000000000000000000006";
    private const string MaterialGuid = "bbbb0000000000000000000000000007";
    private const string UnusedPrefabGuid = "bbbb0000000000000000000000000008";

    private readonly GraphDatabase _db;

    public OntologyQueriesTests()
    {
        _db = GraphDatabase.CreateInMemory();
        using var writer = _db.CreateWriter();
        var enemy = writer.AddAsset(EnemyGuid, "Assets/Scripts/Enemy.cs", "Script", "Assets");
        var boss = writer.AddAsset(BossGuid, "Assets/Scripts/Boss.cs", "Script", "Assets");
        var finalBoss = writer.AddAsset(FinalBossGuid, "Assets/Scripts/FinalBoss.cs", "Script", "Assets");
        var grunt = writer.AddAsset(GruntPrefabGuid, "Assets/Prefabs/Grunt.prefab", "Prefab", "Assets");
        var bossPrefab = writer.AddAsset(BossPrefabGuid, "Assets/Prefabs/Boss.prefab", "Prefab", "Assets");
        var scene = writer.AddAsset(SceneGuid, "Assets/Scenes/Level1.unity", "Scene", "Assets");
        writer.AddAsset(MaterialGuid, "Assets/Materials/Boss.mat", "Material", "Assets");
        writer.AddAsset(UnusedPrefabGuid, "Assets/Prefabs/Unused.prefab", "Prefab", "Assets");
        _ = enemy;

        var bossScript = writer.AddObject(boss, 11500000, 115, "Boss", false);
        var finalBossScript = writer.AddObject(finalBoss, 11500000, 115, "FinalBoss", false);
        var gruntObject = writer.AddObject(grunt, 1, 114, null, false);
        var bossPrefabObject = writer.AddObject(bossPrefab, 1, 114, null, false);
        var sceneObject = writer.AddObject(scene, 1, 1001, null, false);

        writer.AddRef(bossScript, EnemyGuid, 11500000, "Boss", "inherits");
        writer.AddRef(finalBossScript, BossGuid, 11500000, "FinalBoss", "inherits");
        writer.AddRef(gruntObject, EnemyGuid, 11500000, "m_Script", "uses_script");
        writer.AddRef(bossPrefabObject, BossGuid, 11500000, "m_Script", "uses_script");
        writer.AddRef(bossPrefabObject, MaterialGuid, 2100000, "m_Materials[0]", "generic");
        writer.AddRef(sceneObject, GruntPrefabGuid, 100100000, "m_SourcePrefab", "prefab_instance");
        writer.AddRef(sceneObject, BossPrefabGuid, 100100000, "m_SourcePrefab", "prefab_instance");
        writer.AddRef(sceneObject, UnusedPrefabGuid, 1, "m_CorrespondingSourceObject", "corresponding_source");
        writer.Commit();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void DescribeOntologyListsClassesAndRelationSignatures()
    {
        var ontology = GraphQueries.DescribeOntology(_db);

        var prefab = Assert.Single(ontology.Classes, c => c.AssetType == "Prefab");
        Assert.Equal(3, prefab.Count);
        Assert.Equal(3, prefab.BySource["Assets"]);

        Assert.Equal(
            ["generic", "inherits", "prefab_instance", "uses_script"],
            ontology.Relations.Select(r => r.RefKind).Order(StringComparer.Ordinal));
        var usesScript = Assert.Single(ontology.Relations, r => r.RefKind == "uses_script");
        Assert.Equal(2, usesScript.Edges);
        var signature = Assert.Single(usesScript.Signatures);
        Assert.Equal(("Prefab", "Script", 2L), (signature.FromType, signature.ToType, signature.Edges));
    }

    [Fact]
    public void TraverseFindsPrefabsUsingTheClassOrAnySubclass()
    {
        var matches = GraphQueries.Traverse(_db, [EnemyGuid],
        [
            new TraversalStep(TraversalDirection.In, Kinds: ["inherits"], MaxHops: 10, IncludeSelf: true),
            new TraversalStep(TraversalDirection.In, Kinds: ["uses_script"], Types: ["Prefab"]),
        ]);

        Assert.Equal(
            ["Assets/Prefabs/Boss.prefab", "Assets/Prefabs/Grunt.prefab"],
            matches.Select(m => m.Asset.Name));
        var boss = matches[0];
        Assert.Equal("Assets/Scripts/Enemy.cs", boss.Start);
        Assert.Equal(
            [("inherits", TraversalDirection.In, "Assets/Scripts/Boss.cs"),
             ("uses_script", TraversalDirection.In, "Assets/Prefabs/Boss.prefab")],
            boss.Path.Select(h => (h.RefKind, h.Direction, h.Asset)));
        // Grunt.prefab is reached directly from Enemy.cs (the zero-hop include_self).
        Assert.Equal("uses_script", Assert.Single(matches[1].Path).RefKind);
    }

    [Fact]
    public void TraverseWithoutIncludeSelfDropsTheStart()
    {
        var subclasses = GraphQueries.Traverse(_db, [EnemyGuid],
            [new TraversalStep(TraversalDirection.In, Kinds: ["inherits"], MaxHops: 10)]);
        Assert.Equal(
            ["Assets/Scripts/Boss.cs", "Assets/Scripts/FinalBoss.cs"],
            subclasses.Select(m => m.Asset.Name));

        var directOnly = GraphQueries.Traverse(_db, [EnemyGuid],
            [new TraversalStep(TraversalDirection.In, Kinds: ["inherits"])]);
        Assert.Equal(["Assets/Scripts/Boss.cs"], directOnly.Select(m => m.Asset.Name));
    }

    [Fact]
    public void TraverseFromManyStartsStillReachesOtherStarts()
    {
        // Every script that some other script inherits from — starts may be reached too.
        var bases = GraphQueries.Traverse(_db, [EnemyGuid, BossGuid, FinalBossGuid],
            [new TraversalStep(TraversalDirection.Out, Kinds: ["inherits"])]);
        Assert.Equal(
            ["Assets/Scripts/Boss.cs", "Assets/Scripts/Enemy.cs"],
            bases.Select(m => m.Asset.Name));
    }

    [Fact]
    public void TraverseFiltersEndpointTypesButNotIntermediates()
    {
        // Scenes that (transitively) depend on Boss.mat; the prefab in between is any type.
        var scenes = GraphQueries.Traverse(_db, [MaterialGuid],
            [new TraversalStep(TraversalDirection.In, Types: ["scene"], MaxHops: 5)]);
        var scene = Assert.Single(scenes);
        Assert.Equal("Assets/Scenes/Level1.unity", scene.Asset.Name);
        Assert.Equal(2, scene.Path.Count);
    }

    [Fact]
    public void TraverseIgnoresCorrespondingSourceAndUnmatchedKinds()
    {
        Assert.Empty(GraphQueries.Traverse(_db, [SceneGuid],
            [new TraversalStep(TraversalDirection.Out, Kinds: ["corresponding_source"])]));
        Assert.Empty(GraphQueries.Traverse(_db, [SceneGuid],
            [new TraversalStep(TraversalDirection.Out, Types: ["Material"])]));
    }

    [Fact]
    public void FindAssetsFiltersByTypeSourceAndUnreferenced()
    {
        Assert.Equal(
            ["Assets/Prefabs/Boss.prefab", "Assets/Prefabs/Grunt.prefab", "Assets/Prefabs/Unused.prefab"],
            GraphQueries.FindAssets(_db, "", "prefab", null, false, 10).Select(a => a.Name).Order(StringComparer.Ordinal));

        // corresponding_source does not count as a reference.
        var unused = Assert.Single(GraphQueries.FindAssets(_db, "", "Prefab", "Assets", true, 10));
        Assert.Equal("Assets/Prefabs/Unused.prefab", unused.Name);

        Assert.Equal(
            ["Assets/Materials/Boss.mat", "Assets/Prefabs/Boss.prefab", "Assets/Scripts/Boss.cs"],
            GraphQueries.FindAssets(_db, "/boss.", null, null, false, 10).Select(a => a.Name).Order(StringComparer.Ordinal));
        Assert.Empty(GraphQueries.FindAssets(_db, "", "Prefab", "PackageCache", false, 10));
    }
}
