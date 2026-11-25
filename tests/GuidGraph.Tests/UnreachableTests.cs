using System.Text.Json;
using GuidGraph.Graph;

namespace GuidGraph.Tests;

public sealed class UnreachableTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"guidgraph-unreach-{Guid.NewGuid():N}.db");

    private const string BuildSettings = "ProjectSettings/EditorBuildSettings.asset";

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    private static string G(int n) => $"bbbb{n:D28}";

    /// <summary>
    /// Main (in the build) -> A -> B (nested), A -[generic]-> E. Test (not in the build) -> T.
    /// Nothing references C; only C references D. Resources/R is referenced by nothing.
    /// </summary>
    private GraphDatabase CreateDatabase(bool withScenes = true, int extraOrphans = 0, int nameLength = 0)
    {
        var db = GraphDatabase.CreateFile(_dbPath);
        using var writer = db.CreateWriter();
        long Add(int n, string path, string type) => writer.AddAsset(G(n), path, type, "Assets");
        void Link(long asset, long fileId, int target, string path, string kind)
        {
            var obj = writer.AddObject(asset, fileId, 1, null, false);
            writer.AddRef(obj, G(target), 100100000, path, kind);
        }

        var a = Add(3, "Assets/A.prefab", "Prefab");
        Add(4, "Assets/B.prefab", "Prefab");
        var c = Add(5, "Assets/C.prefab", "Prefab");
        Add(6, "Assets/D.prefab", "Prefab");
        Add(7, "Assets/E.prefab", "Prefab");
        Add(8, "Assets/T.prefab", "Prefab");
        Add(9, "Assets/Resources/R.prefab", "Prefab");
        Link(a, 1, 4, "m_SourcePrefab", "prefab_instance");
        Link(a, 2, 7, "target", "generic");
        Link(c, 1, 6, "m_SourcePrefab", "prefab_instance");
        if (withScenes)
        {
            Link(Add(1, "Assets/Main.unity", "Scene"), 1, 3, "m_SourcePrefab", "prefab_instance");
            Link(Add(2, "Assets/Test.unity", "Scene"), 1, 8, "m_SourcePrefab", "prefab_instance");
            var settings = writer.AddAsset(BuildSettings, BuildSettings, "ProjectSettings", "ProjectSettings");
            Link(settings, 1, 1, "m_Scenes[0].guid", "build_settings");
        }

        for (var i = 0; i < extraOrphans; i++)
        {
            Add(100 + i, $"Assets/Extra/{new string('x', nameLength)}{i:D4}.prefab", "Prefab");
        }

        writer.Commit();
        return db;
    }

    private static JsonElement Run(GraphDatabase db, UnreachableQuery? query = null) =>
        JsonDocument.Parse(UnreachableSearch.RunJson(db, query ?? new UnreachableQuery())).RootElement;

    private static List<string> Names(JsonElement result) =>
        result.GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("name").GetString()!).ToList();

    private static JsonElement Match(JsonElement result, string name) =>
        result.GetProperty("matches").EnumerateArray().Single(m => m.GetProperty("name").GetString() == name);

    [Fact]
    public void ListsOnlyPrefabsNoSceneReaches()
    {
        using var db = CreateDatabase();

        var result = Run(db);

        Assert.Equal(["Assets/C.prefab", "Assets/D.prefab", "Assets/Resources/R.prefab"], Names(result));
        Assert.Equal(7, result.GetProperty("candidates").GetInt32());
        Assert.Equal(4, result.GetProperty("reachable").GetInt32());
        Assert.Equal(3, result.GetProperty("unreachable").GetInt32());
        Assert.Equal(2, result.GetProperty("roots").GetProperty("count").GetInt32());
        Assert.False(result.TryGetProperty("truncated", out _));
        Assert.Contains("Resources.Load", result.GetProperty("notes")[0].GetString());
    }

    [Fact]
    public void ReferrersTellOrphansFromOrphanSubtrees()
    {
        using var db = CreateDatabase();

        var result = Run(db);

        var orphan = Match(result, "Assets/C.prefab");
        Assert.Equal(0, orphan.GetProperty("referrers").GetInt32());
        Assert.False(orphan.TryGetProperty("referrer_samples", out _));
        var below = Match(result, "Assets/D.prefab");
        Assert.Equal(1, below.GetProperty("referrers").GetInt32());
        Assert.Equal("Assets/C.prefab", below.GetProperty("referrer_samples")[0].GetString());
        Assert.Contains("Resources", Match(result, "Assets/Resources/R.prefab").GetProperty("notes")[0].GetString());
    }

    [Fact]
    public void BuildOnlyStartsFromBuildScenes()
    {
        using var db = CreateDatabase();

        var result = Run(db, new UnreachableQuery { BuildOnly = true });

        Assert.Contains("Assets/T.prefab", Names(result));
        Assert.Equal(1, result.GetProperty("roots").GetProperty("count").GetInt32());
        Assert.True(result.GetProperty("roots").GetProperty("build_only").GetBoolean());
        Assert.Equal(2, result.GetProperty("notes").GetArrayLength());
    }

    [Fact]
    public void BuildOnlyWithoutSceneRootsIsIgnoredWithAWarning()
    {
        using var db = CreateDatabase();

        var result = Run(db, new UnreachableQuery { BuildOnly = true, Roots = ["Prefab"], Types = ["Scene"] });

        Assert.Contains("build_only", result.GetProperty("warnings")[0].GetString());
        Assert.False(result.GetProperty("roots").TryGetProperty("build_only", out _));
    }

    [Fact]
    public void KindsRestrictTheEdgesFollowed()
    {
        using var db = CreateDatabase();

        var result = Run(db, new UnreachableQuery { Kinds = ["prefab_instance"] });

        Assert.Contains("Assets/E.prefab", Names(result));
        Assert.Equal(0, Match(result, "Assets/E.prefab").GetProperty("referrers").GetInt32());
        Assert.DoesNotContain("Assets/B.prefab", Names(result));
    }

    [Fact]
    public void PathContainsNarrowsTheCandidates()
    {
        using var db = CreateDatabase();

        var result = Run(db, new UnreachableQuery { PathContains = ["Resources/"] });

        Assert.Equal(1, result.GetProperty("candidates").GetInt32());
        Assert.Equal(["Assets/Resources/R.prefab"], Names(result));
    }

    [Fact]
    public void LimitTruncatesButCountsEverything()
    {
        using var db = CreateDatabase();

        var result = Run(db, new UnreachableQuery { Limit = 2 });

        Assert.Equal(2, result.GetProperty("matches").GetArrayLength());
        Assert.True(result.GetProperty("truncated").GetBoolean());
        Assert.Equal(3, result.GetProperty("unreachable").GetInt32());
    }

    [Fact]
    public void OversizedAnswerListsFewerMatches()
    {
        using var db = CreateDatabase(extraOrphans: 200, nameLength: 250);

        var json = UnreachableSearch.RunJson(db, new UnreachableQuery { Limit = 200 });
        var result = JsonDocument.Parse(json).RootElement;

        Assert.True(json.Length <= GraphSearch.MaxOutputChars);
        Assert.True(result.GetProperty("truncated").GetBoolean());
        Assert.Equal(203, result.GetProperty("unreachable").GetInt32());
        Assert.Contains("capped", result.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public void NoRootsIsAnError()
    {
        using var db = CreateDatabase(withScenes: false);

        Assert.Equal("no_roots", Run(db).GetProperty("error").GetString());
    }

    [Fact]
    public void UnknownTypesAndKindsWarn()
    {
        using var db = CreateDatabase();

        var result = Run(db, new UnreachableQuery { Types = ["Prefabb"], Kinds = ["nope"] });

        var warnings = result.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToList();
        Assert.Contains(warnings, w => w.Contains("nope") && w.Contains("describe_ontology"));
        Assert.Contains(warnings, w => w.Contains("Prefabb") && w.Contains("describe_ontology"));
    }

    [Fact]
    public void LeavesTheDatabaseUnchanged()
    {
        CreateDatabase().Dispose();
        var before = File.ReadAllBytes(_dbPath);

        using (var db = GraphDatabase.OpenFile(_dbPath))
        {
            Run(db);
        }

        Assert.Equal(before, File.ReadAllBytes(_dbPath));
    }
}
