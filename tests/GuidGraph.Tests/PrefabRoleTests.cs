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
    public void SearchMatchesCarryRoleOnlyForWrappers()
    {
        using var db = CreateDatabase();

        var json = GraphSearch.RunJson(db, new SearchQuery { From = new SearchStart { Types = ["Prefab", "Material"] } });
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var roles = doc.RootElement.GetProperty("matches").EnumerateArray().ToDictionary(
            m => m.GetProperty("name").GetString()!,
            m => m.TryGetProperty("role", out var r) ? r.GetString() : null);

        Assert.Equal("wrapper", roles["Assets/Wrapper.prefab"]);
        Assert.Null(roles["Assets/WithScript.prefab"]); // leaf is the absence of a role
        Assert.Null(roles["Assets/M.mat"]);
    }

    [Fact]
    public void BinaryBodyTypesAreMarkedUnparsed()
    {
        Assert.False(OntologyGlossary.IsParsed("TerrainData"));
        Assert.False(OntologyGlossary.IsParsed("Texture"));
        Assert.True(OntologyGlossary.IsParsed("Prefab"));
        Assert.True(OntologyGlossary.IsParsed("ScriptableObject"));
    }

    [Fact]
    public void SearchListsUnparsedTypesOfItsResults()
    {
        using var db = CreateDatabase();
        using var writer = db.CreateWriter();
        writer.AddAsset("aaaa0000000000000000000000000009", "Assets/Terrain/T.asset", "TerrainData", "Assets");
        writer.Commit();

        var json = GraphSearch.RunJson(db, new SearchQuery { From = new SearchStart { Types = ["TerrainData", "Prefab"] } });
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(["TerrainData"], doc.RootElement.GetProperty("unsupported").EnumerateArray().Select(t => t.GetString()));

        var prefabsOnly = GraphSearch.RunJson(db, new SearchQuery { From = new SearchStart { Types = ["Prefab"] } });
        using var doc2 = System.Text.Json.JsonDocument.Parse(prefabsOnly);
        Assert.False(doc2.RootElement.TryGetProperty("unsupported", out _));
    }

    [Fact]
    public void OversizedAnswersAreCutDownAndSaySo()
    {
        using var db = CreateDatabase();
        using var writer = db.CreateWriter();
        for (var i = 0; i < 1500; i++)
        {
            writer.AddAsset($"bbbb{i:D28}", $"Assets/Bulk/{new string((char)97, 220)}_{i:D5}.prefab", "Prefab", "Assets");
        }

        writer.Commit();

        var json = GraphSearch.RunJson(db, new SearchQuery { From = new SearchStart { Types = ["Prefab"] }, Limit = 200 });
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        Assert.True(json.Length <= GraphSearch.MaxOutputChars, $"length {json.Length}");
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Contains("output capped", doc.RootElement.GetProperty("warnings")[0].GetString());
        Assert.Equal(1503, doc.RootElement.GetProperty("total_matches").GetInt32());
    }
}
