using GuidGraph.Graph;

namespace GuidGraph.Tests;

public sealed class PrefabRoleTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"guidgraph-role-{Guid.NewGuid():N}.db");
    private const string Wrapper = "aaaa0000000000000000000000000001";
    private const string WithScript = "aaaa0000000000000000000000000002";
    private const string Plain = "aaaa0000000000000000000000000003";
    private const string Material = "aaaa0000000000000000000000000004";

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    private GraphDatabase CreateDatabase()
    {
        var db = GraphDatabase.CreateFile(_dbPath);
        using var writer = db.CreateWriter();
        var wrapper = writer.AddAsset(Wrapper, "Assets/Wrapper.prefab", "Prefab", "Assets");
        var withScript = writer.AddAsset(WithScript, "Assets/WithScript.prefab", "Prefab", "Assets");
        var plain = writer.AddAsset(Plain, "Assets/Plain.prefab", "Prefab", "Assets");
        writer.AddAsset(Material, "Assets/M.mat", "Material", "Assets");

        // Wrapper: a root GameObject + Transform, two nested instances and their stripped placeholders
        // (including a stripped MonoBehaviour, which belongs to the nested prefab, not to the wrapper).
        writer.AddObject(wrapper, 1, 1, null, false);
        writer.AddObject(wrapper, 2, 4, null, false);
        writer.AddObject(wrapper, 3, 1001, null, false);
        writer.AddObject(wrapper, 4, 1001, null, false);
        writer.AddObject(wrapper, 5, 4, null, true);
        writer.AddObject(wrapper, 6, 114, null, true);

        // Has a nested instance but also its own MonoBehaviour.
        writer.AddObject(withScript, 1, 1, null, false);
        writer.AddObject(withScript, 2, 4, null, false);
        writer.AddObject(withScript, 3, 114, null, false);
        writer.AddObject(withScript, 4, 1001, null, false);

        // No nested instances at all.
        writer.AddObject(plain, 1, 1, null, false);
        writer.AddObject(plain, 2, 224, null, false);
        writer.Commit();
        return db;
    }

    [Fact]
    public void WrapperHasOnlyNestedInstancesAndTransforms()
    {
        using var db = CreateDatabase();

        var roles = GraphQueries.PrefabRoles(db, [Wrapper, WithScript, Plain, Material]);

        Assert.Equal("wrapper", roles[Wrapper]);
        Assert.Equal("leaf", roles[WithScript]);
        Assert.Equal("leaf", roles[Plain]);
        Assert.False(roles.ContainsKey(Material)); // only prefabs get a role
    }

    [Fact]
    public void EmptyInputYieldsNoRoles()
    {
        using var db = CreateDatabase();

        Assert.Empty(GraphQueries.PrefabRoles(db, []));
    }

    [Fact]
    public void SearchMatchesCarryRoleForPrefabsOnly()
    {
        using var db = CreateDatabase();

        var json = GraphSearch.RunJson(db, new SearchQuery { From = new SearchStart { Types = ["Prefab", "Material"] } });
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var roles = doc.RootElement.GetProperty("matches").EnumerateArray().ToDictionary(
            m => m.GetProperty("name").GetString()!,
            m => m.TryGetProperty("role", out var r) ? r.GetString() : null);

        Assert.Equal("wrapper", roles["Assets/Wrapper.prefab"]);
        Assert.Equal("leaf", roles["Assets/WithScript.prefab"]);
        Assert.Null(roles["Assets/M.mat"]);
    }
}
