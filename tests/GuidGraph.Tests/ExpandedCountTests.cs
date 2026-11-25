using GuidGraph.Graph;

namespace GuidGraph.Tests;

public sealed class ExpandedCountTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"guidgraph-expand-{Guid.NewGuid():N}.db");

    private const string Scene = "aaaa0000000000000000000000000001";
    private const string Tree = "aaaa0000000000000000000000000002";
    private const string Forest = "aaaa0000000000000000000000000003";
    private const string Leaf = "aaaa0000000000000000000000000004";
    private const string Material = "aaaa0000000000000000000000000005";
    private const string Other = "aaaa0000000000000000000000000006";

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Scene places Forest twice and Tree once directly. Forest places Tree three times. Tree has two renderers
    /// using Material and one using Other; Tree's renderers 12, 13 (on GameObject 10) and 22 (on its child GameObject 20)
    /// use Material. The scene removes Renderer 12 from its direct Tree instance.
    /// </summary>
    private GraphDatabase CreateDatabase(bool withRemoval)
    {
        var db = GraphDatabase.CreateFile(_dbPath);
        using var writer = db.CreateWriter();
        var scene = writer.AddAsset(Scene, "Assets/S.unity", "Scene", "Assets");
        var tree = writer.AddAsset(Tree, "Assets/Tree.prefab", "Prefab", "Assets");
        var forest = writer.AddAsset(Forest, "Assets/Forest.prefab", "Prefab", "Assets");
        writer.AddAsset(Leaf, "Assets/Leaf.prefab", "Prefab", "Assets");
        writer.AddAsset(Material, "Assets/M.mat", "Material", "Assets");
        writer.AddAsset(Other, "Assets/O.mat", "Material", "Assets");

        // Tree: root GameObject 10 (Transform 11 listing child transform 21; Renderers 12 and 13 on it) and
        // child GameObject 20 (Transform 21, Renderer 22). Renderer 30 uses Other.
        writer.AddObject(tree, 10, 1, null, false);
        writer.AddObject(tree, 20, 1, null, false);
        var transform = writer.AddObject(tree, 11, 4, null, false);
        writer.AddRef(transform, null, 10, "m_GameObject", "generic");
        writer.AddRef(transform, null, 21, "m_Children[0]", "generic");
        var childTransform = writer.AddObject(tree, 21, 4, null, false);
        writer.AddRef(childTransform, null, 20, "m_GameObject", "generic");
        foreach (var (fileId, gameObject, material) in new[] { (12L, 10L, Material), (13L, 10L, Material), (22L, 20L, Material), (30L, 10L, Other) })
        {
            var renderer = writer.AddObject(tree, fileId, 23, null, false);
            writer.AddRef(renderer, null, gameObject, "m_GameObject", "generic");
            writer.AddRef(renderer, material, 2100000, "m_Materials[0]", "generic");
        }

        // Forest: three instances of Tree.
        for (var i = 1; i <= 3; i++)
        {
            var instance = writer.AddObject(forest, 100 + i, 1001, null, false);
            writer.AddRef(instance, Tree, 100100000, "m_SourcePrefab", "prefab_instance");
            writer.AddRef(instance, Tree, 12, "m_Modification.m_Modifications[0].target", "prefab_modification");
        }

        // Scene: two Forests and one direct Tree.
        var sceneInstances = new[] { (201L, Forest), (202L, Forest), (203L, Tree) };
        foreach (var (fileId, source) in sceneInstances)
        {
            var instance = writer.AddObject(scene, fileId, 1001, null, false);
            writer.AddRef(instance, source, 100100000, "m_SourcePrefab", "prefab_instance");
            if (withRemoval && fileId == 203)
            {
                writer.AddRef(instance, Tree, 12, "m_Modification.m_RemovedComponents[0]", "generic");
                writer.AddRef(instance, Tree, 20, "m_Modification.m_RemovedGameObjects[0]", "generic");
                writer.AddRef(instance, Tree, 999, "m_Modification.m_RemovedComponents[1]", "generic");
            }
        }

        writer.RebuildPlacements();
        writer.Commit();
        return db;
    }

    [Fact]
    public void MultipliesThroughNestedPlacements()
    {
        using var db = CreateDatabase(withRemoval: false);

        var result = ExpandedCounter.Count(db, Material, Scene, maxDepth: 10);

        // Per Tree: renderers 12, 13, 22 use Material = 3. Trees in the scene: 1 direct + 2 Forests x 3 = 7.
        Assert.Equal(0, result.Direct);
        Assert.Equal(21, result.ViaNested);
        Assert.Equal(21, result.ExpandedCount);
        Assert.False(result.DepthLimited);
    }

    [Fact]
    public void PrefabsAreCountedByTheirPlacements()
    {
        using var db = CreateDatabase(withRemoval: false);

        Assert.Equal(7, ExpandedCounter.Count(db, Tree, Scene, 10).ExpandedCount);
        Assert.Equal(2, ExpandedCounter.Count(db, Forest, Scene, 10).ExpandedCount);
        Assert.Equal(0, ExpandedCounter.Count(db, Leaf, Scene, 10).ExpandedCount);
    }

    [Fact]
    public void DepthLimitStopsTheExpansionAndSaysSo()
    {
        using var db = CreateDatabase(withRemoval: false);

        var result = ExpandedCounter.Count(db, Material, Scene, maxDepth: 1);

        Assert.True(result.DepthLimited);
        Assert.Equal(3, result.ViaNested); // only the one directly placed Tree
    }

    [Fact]
    public void RemovedComponentsAndGameObjectsAreSubtracted()
    {
        using var db = CreateDatabase(withRemoval: true);

        var result = ExpandedCounter.Count(db, Material, Scene, maxDepth: 10);

        // Component 12 (1) and GameObject 20 with its renderer 22 (1) come off the one direct Tree; id 999 is unknown.
        Assert.Equal(2, result.Removed);
        Assert.Equal(1, result.UnresolvedRemovals);
        Assert.Equal(19, result.ExpandedCount);
    }
}
