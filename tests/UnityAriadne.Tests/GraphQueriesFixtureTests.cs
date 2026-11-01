using UnityAriadne.Graph;

namespace UnityAriadne.Tests;

/// <summary>
/// Phase 4 query layer against the real fixture graph. Every expectation here is
/// PackageCache-independent: the fog-material neighborhood is entirely Assets-source
/// (verified 2026-07-08), and edges to missing package assets still exist as rows
/// pointing at Unresolved assets.
/// </summary>
[Collection("fixture-graph")]
public class GraphQueriesFixtureTests(FixtureGraphFixture fixture)
{
    private const string FogMaterialPath = "Assets/Art/Fog/Materials/HeightFogOriginal_FreeCam.mat";

    /// <summary>The best-known dangling GUID from trimming the source project.</summary>
    private const string DanglingGuid = "299147e0b236560478d85010e9e1c387";

    private GraphDatabase Db => fixture.Db;

    [FixtureFact]
    public void ResolvesFogMaterialByFileName()
    {
        var resolved = Assert.Single(GraphQueries.ResolveAssets(Db, "HeightFogOriginal_FreeCam.mat"));
        Assert.Equal(FogMaterialPath, resolved.Path);
        Assert.Equal(("Material", "Assets"), (resolved.AssetType, resolved.Source));
    }

    [FixtureFact]
    public void FogMaterialHasSixReferrers()
    {
        var guid = GraphQueries.ResolveAssets(Db, FogMaterialPath).Single().Guid;
        var references = GraphQueries.FindReferences(Db, guid);

        Assert.Equal(
            [
                "Assets/Data/Free Camera Settings.asset",
                "Assets/URP/Settings/Desktop Low Renderer.asset",
                "Assets/URP/Settings/Desktop Renderer.asset",
                "Assets/URP/Settings/Mobile High Renderer.asset",
                "Assets/URP/Settings/Mobile Low Renderer.asset",
                "Assets/URP/Settings/Mobile Medium Renderer.asset",
            ],
            references.Select(r => r.Referrer.Path).Order(StringComparer.Ordinal));
        Assert.All(references, r => Assert.Equal(("generic", 1L), (r.RefKind, r.RefCount)));
    }

    [FixtureFact]
    public void FogMaterialImpactReachesPipelineAssetsAtDistanceTwo()
    {
        var guid = GraphQueries.ResolveAssets(Db, FogMaterialPath).Single().Guid;
        var impacted = GraphQueries.ImpactAnalysis(Db, guid, maxDepth: 2);

        Assert.Equal(6, impacted.Count(i => i.Distance == 1));
        Assert.Equal(
            [
                "Assets/Prefabs/RTSCameraController.prefab",
                "Assets/URP/Settings/Desktop Default.asset",
                "Assets/URP/Settings/Desktop Low.asset",
                "Assets/URP/Settings/Desktop.asset",
                "Assets/URP/Settings/Mobile High.asset",
                "Assets/URP/Settings/Mobile Low.asset",
                "Assets/URP/Settings/Mobile Medium.asset",
            ],
            impacted.Where(i => i.Distance == 2).Select(i => i.Asset.Path));

        // Distance 1 assets were discovered via the target itself.
        Assert.All(impacted.Where(i => i.Distance == 1), i => Assert.Equal(FogMaterialPath, i.Via));
    }

    [FixtureFact]
    public void FogMaterialNeighborhoodIsReferrersPlusShadergraph()
    {
        var guid = GraphQueries.ResolveAssets(Db, FogMaterialPath).Single().Guid;
        var neighborhood = GraphQueries.GetNeighbors(Db, guid, hops: 1);

        // 6 referrers + the material + its single dependency (HeightFog.shadergraph).
        Assert.Equal(8, neighborhood.Assets.Count);
        Assert.Contains(neighborhood.Assets, a => a.Path == "Assets/Scripts/Fog/HeightFog.shadergraph");
        Assert.Equal(7, neighborhood.Edges.Count);
        Assert.Contains(neighborhood.Edges,
            e => e.FromName == FogMaterialPath && e.ToName == "Assets/Scripts/Fog/HeightFog.shadergraph");
    }

    [FixtureFact]
    public void KnownDanglingGuidHasSeventeenAssetsReferrers()
    {
        var resolved = Assert.Single(GraphQueries.ResolveAssets(Db, DanglingGuid));
        Assert.Null(resolved.Path);
        Assert.Equal("Unresolved", resolved.Source);

        var references = GraphQueries.FindReferences(Db, DanglingGuid);
        Assert.Equal(17, references.Count);
        Assert.Equal(31, references.Sum(r => r.RefCount));
        Assert.All(references, r => Assert.Equal("Assets", r.Referrer.Source));
    }

    [FixtureFact]
    public void SearchNodesFindsTheFogMaterial()
    {
        var hits = GraphQueries.SearchNodes(Db, "heightfogoriginal", limit: 10);
        Assert.Contains(hits, a => a.Path == FogMaterialPath);
    }
}
