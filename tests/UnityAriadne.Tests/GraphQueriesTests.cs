using UnityAriadne.Graph;

namespace UnityAriadne.Tests;

/// <summary>
/// Semantics of the Phase 4 query layer on a small synthetic graph:
///
///   Main.unity ──prefab_instance──► Hero.prefab ──generic(×2)──► Sky.mat ──generic──► Cloud%_x.png
///                                        │                                       ▲
///                                        └──generic──► (dead guid)               │
///   Main.unity ──corresponding_source──────────────────────────────────────────┘  (excluded everywhere)
/// </summary>
public class GraphQueriesTests : IDisposable
{
    private const string SceneGuid = "aaaa0000000000000000000000000001";
    private const string PrefabGuid = "aaaa0000000000000000000000000002";
    private const string MaterialGuid = "aaaa0000000000000000000000000003";
    private const string OtherMaterialGuid = "aaaa0000000000000000000000000004";
    private const string TextureGuid = "aaaa0000000000000000000000000005";
    private const string DeadGuid = "dead0000000000000000000000000001";

    private readonly GraphDatabase _db;

    public GraphQueriesTests()
    {
        _db = GraphDatabase.CreateInMemory();
        using var writer = _db.CreateWriter();
        var scene = writer.AddAsset(SceneGuid, "Assets/Scenes/Main.unity", "Scene", "Assets");
        var prefab = writer.AddAsset(PrefabGuid, "Assets/Prefabs/Hero.prefab", "Prefab", "Assets");
        var material = writer.AddAsset(MaterialGuid, "Assets/Materials/Sky.mat", "Material", "Assets");
        writer.AddAsset(OtherMaterialGuid, "Assets/Other/sky.mat", "Material", "Assets");
        writer.AddAsset(TextureGuid, "Assets/Textures/Cloud%_x.png", "Texture", "Assets");
        writer.AddAsset(DeadGuid, null, "Unknown", "Unresolved");

        var sceneObject = writer.AddObject(scene, 1, 1, null, false);
        var prefabObject = writer.AddObject(prefab, 1, 1, null, false);
        var materialObject = writer.AddObject(material, 1, 21, null, false);

        writer.AddRef(sceneObject, PrefabGuid, 100, "m_SourcePrefab", "prefab_instance");
        writer.AddRef(prefabObject, MaterialGuid, 2100000, "m_Materials[0]", "generic");
        writer.AddRef(prefabObject, MaterialGuid, 2100000, "m_Materials[1]", "generic");
        writer.AddRef(prefabObject, DeadGuid, 2100000, "m_Materials[2]", "generic");
        writer.AddRef(materialObject, TextureGuid, 2800000, "m_Texture", "generic");
        writer.AddRef(sceneObject, TextureGuid, 2800000, "m_CorrespondingSourceObject", "corresponding_source");
        writer.Commit();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void ResolvesByExactGuidFirst()
    {
        var resolved = Assert.Single(GraphQueries.ResolveAssets(_db, MaterialGuid));
        Assert.Equal("Assets/Materials/Sky.mat", resolved.Path);
        Assert.Equal("Material", resolved.AssetType);
    }

    [Fact]
    public void ResolvesByExactPath()
    {
        var resolved = Assert.Single(GraphQueries.ResolveAssets(_db, "Assets/Other/sky.mat"));
        Assert.Equal(OtherMaterialGuid, resolved.Guid);
    }

    [Fact]
    public void FilenameSuffixMatchIsCaseInsensitiveAndReportsAmbiguity()
    {
        // Both Sky.mat and sky.mat match the segment-boundary suffix — callers see
        // the ambiguity instead of a silent pick.
        Assert.Equal(
            ["Assets/Materials/Sky.mat", "Assets/Other/sky.mat"],
            GraphQueries.ResolveAssets(_db, "Sky.mat").Select(a => a.Path));

        // A longer suffix disambiguates.
        var resolved = Assert.Single(GraphQueries.ResolveAssets(_db, "Materials/Sky.mat"));
        Assert.Equal(MaterialGuid, resolved.Guid);
    }

    [Fact]
    public void SuffixMatchRequiresSegmentBoundary()
    {
        // "ky.mat" is not a path segment — it must not match Sky.mat.
        Assert.Empty(GraphQueries.ResolveAssets(_db, "ky.mat"));
        Assert.Empty(GraphQueries.ResolveAssets(_db, "no-such-asset"));
    }

    [Fact]
    public void FindReferencesAggregatesRefCountAndExcludesCorrespondingSource()
    {
        var reference = Assert.Single(GraphQueries.FindReferences(_db, MaterialGuid));
        Assert.Equal("Assets/Prefabs/Hero.prefab", reference.Referrer.Path);
        Assert.Equal(("generic", 2L), (reference.RefKind, reference.RefCount));

        // The scene's corresponding_source ref must not appear.
        var textureRef = Assert.Single(GraphQueries.FindReferences(_db, TextureGuid));
        Assert.Equal("Assets/Materials/Sky.mat", textureRef.Referrer.Path);
    }

    [Fact]
    public void FindDependenciesListsUnresolvedTargets()
    {
        var dependencies = GraphQueries.FindDependencies(_db, PrefabGuid);
        Assert.Equal(2, dependencies.Count);
        Assert.Equal("Assets/Materials/Sky.mat", dependencies[0].Target.Path);
        Assert.Equal(2, dependencies[0].RefCount);
        Assert.Null(dependencies[1].Target.Path);
        Assert.Equal(DeadGuid, dependencies[1].Target.Name);
    }

    [Fact]
    public void ImpactAnalysisWalksReverseEdgesByDistance()
    {
        Assert.Collection(
            GraphQueries.ImpactAnalysis(_db, TextureGuid, maxDepth: 5),
            impacted =>
            {
                Assert.Equal(("Assets/Materials/Sky.mat", 1), (impacted.Asset.Path, impacted.Distance));
                Assert.Equal("Assets/Textures/Cloud%_x.png", impacted.Via);
            },
            impacted =>
            {
                Assert.Equal(("Assets/Prefabs/Hero.prefab", 2), (impacted.Asset.Path, impacted.Distance));
                Assert.Equal("Assets/Materials/Sky.mat", impacted.Via);
            },
            impacted =>
            {
                // Reached at distance 3 via the prefab — the scene's own
                // corresponding_source shortcut to the texture is excluded.
                Assert.Equal(("Assets/Scenes/Main.unity", 3), (impacted.Asset.Path, impacted.Distance));
                Assert.Equal("Assets/Prefabs/Hero.prefab", impacted.Via);
            });
    }

    [Fact]
    public void ImpactAnalysisRespectsMaxDepthAndUnknownGuid()
    {
        var impacted = Assert.Single(GraphQueries.ImpactAnalysis(_db, TextureGuid, maxDepth: 1));
        Assert.Equal("Assets/Materials/Sky.mat", impacted.Asset.Path);

        Assert.Empty(GraphQueries.ImpactAnalysis(_db, "ffff0000000000000000000000000000", maxDepth: 3));
    }

    [Fact]
    public void GetNeighborsReturnsInducedSubgraph()
    {
        var neighborhood = GraphQueries.GetNeighbors(_db, MaterialGuid, hops: 1);

        Assert.Equal(
            ["Assets/Materials/Sky.mat", "Assets/Prefabs/Hero.prefab", "Assets/Textures/Cloud%_x.png"],
            neighborhood.Assets.Select(a => a.Path));
        Assert.Equal(
            [
                "Assets/Materials/Sky.mat -> Assets/Textures/Cloud%_x.png (generic x1)",
                "Assets/Prefabs/Hero.prefab -> Assets/Materials/Sky.mat (generic x2)",
            ],
            neighborhood.Edges.Select(e => $"{e.FromName} -> {e.ToName} ({e.RefKind} x{e.RefCount})"));
    }

    [Fact]
    public void GetNeighborsHopCountIgnoresExcludedShortcuts()
    {
        // Texture ◄ material ◄ prefab: two hops. The scene's corresponding_source
        // shortcut to the texture is excluded, so scene and dead guid are 3 hops away.
        var twoHops = GraphQueries.GetNeighbors(_db, TextureGuid, hops: 2);
        Assert.Equal(
            ["Assets/Materials/Sky.mat", "Assets/Prefabs/Hero.prefab", "Assets/Textures/Cloud%_x.png"],
            twoHops.Assets.Select(a => a.Path));
        Assert.Equal(2, twoHops.Edges.Count);

        var threeHops = GraphQueries.GetNeighbors(_db, TextureGuid, hops: 3);
        Assert.Equal(5, threeHops.Assets.Count);
        Assert.Contains(threeHops.Assets, a => a.Guid == DeadGuid);
        Assert.Contains(threeHops.Assets, a => a.Guid == SceneGuid);
        Assert.Equal(4, threeHops.Edges.Count);
    }

    [Fact]
    public void SearchNodesMatchesSubstringsCaseInsensitively()
    {
        // Shortest path first, so the most specific names surface under a tight limit.
        Assert.Equal(
            ["Assets/Other/sky.mat", "Assets/Materials/Sky.mat"],
            GraphQueries.SearchNodes(_db, "SKY", limit: 10).Select(a => a.Path));

        Assert.Equal(
            ["Assets/Other/sky.mat"],
            GraphQueries.SearchNodes(_db, "SKY", limit: 1).Select(a => a.Path));
    }

    [Fact]
    public void SearchNodesTreatsLikeWildcardsLiterally()
    {
        var hit = Assert.Single(GraphQueries.SearchNodes(_db, "Cloud%_x", limit: 10));
        Assert.Equal(TextureGuid, hit.Guid);

        // Unescaped, "Cloud%x" would match via the % wildcard — it must not.
        Assert.Empty(GraphQueries.SearchNodes(_db, "Cloud%x", limit: 10));
    }

    [Fact]
    public void SearchNodesFindsUnresolvedGuidByExactMatch()
    {
        var hit = Assert.Single(GraphQueries.SearchNodes(_db, DeadGuid, limit: 10));
        Assert.Equal("Unresolved", hit.Source);
        Assert.Null(hit.Path);
    }
}
