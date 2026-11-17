using System.Text.Json;
using UnityAriadne.Graph;

namespace UnityAriadne.Tests;

/// <summary>
/// The integrated search (<see cref="GraphSearch"/>) on a small graph with a code layer,
/// folders that carry Unity conventions, and doc comments:
///
///   Enemy.cs ◄─inherits── Boss.cs            (Assets/Game/Scripts/…, Boss.cs documented "ボスの行動")
///      ▲                     ▲
///   uses_script           uses_script
///   Grunt.prefab          Boss.prefab        (Assets/Game/Prefabs/Enemies/…)
///      ▲                     ▲
///      └─prefab_instance── Level.unity ──generic──► Sky.mat
///   Popup.prefab (Assets/Game/Resources/UI/, unreferenced)    Editor/EnemyInspector.cs ─custom_editor─► Enemy.cs
/// </summary>
public class GraphSearchTests : IDisposable
{
    private readonly GraphDatabase _db = GraphDatabase.CreateInMemory();

    public GraphSearchTests()
    {
        using var writer = _db.CreateWriter();
        var enemy = writer.AddAsset("e1", "Assets/Game/Scripts/Enemy.cs", "Script", "Assets");
        var boss = writer.AddAsset("e2", "Assets/Game/Scripts/Boss.cs", "Script", "Assets");
        var inspector = writer.AddAsset("e3", "Assets/Game/Editor/EnemyInspector.cs", "Script", "Assets");
        var grunt = writer.AddAsset("p1", "Assets/Game/Prefabs/Enemies/Grunt.prefab", "Prefab", "Assets");
        var bossPrefab = writer.AddAsset("p2", "Assets/Game/Prefabs/Enemies/Boss.prefab", "Prefab", "Assets");
        writer.AddAsset("p3", "Assets/Game/Resources/UI/Popup.prefab", "Prefab", "Assets");
        var level = writer.AddAsset("s1", "Assets/Game/Scenes/Level.unity", "Scene", "Assets");
        writer.AddAsset("m1", "Assets/Game/Materials/Sky.mat", "Material", "Assets");

        Ref(writer, boss, 11500000, "e1", "inherits");
        Ref(writer, inspector, 11500000, "e1", "custom_editor");
        Ref(writer, grunt, 1, "e1", "uses_script");
        Ref(writer, bossPrefab, 1, "e2", "uses_script");
        Ref(writer, level, 1, "p1", "prefab_instance");
        Ref(writer, level, 1, "p2", "prefab_instance");
        Ref(writer, level, 1, "m1", "generic");

        writer.AddCodeSymbol(enemy, "Game.Enemy", "class", "敵の基底クラス");
        writer.AddCodeSymbol(boss, "Game.Boss", "class", "ボスの行動を制御する");
        writer.AddCodeSymbol(inspector, "Game.EnemyInspector", "class", null);
        writer.Commit();
    }

    public void Dispose() => _db.Dispose();

    private long _nextFileId = 1;

    private void Ref(GraphWriter writer, long assetId, long classId, string toGuid, string kind)
    {
        var objectId = writer.AddObject(assetId, _nextFileId++, (int)Math.Min(classId, 1001), null, false);
        writer.AddRef(objectId, toGuid, 11500000, "m_Ref", kind);
    }

    private JsonElement Run(string queryJson)
    {
        var query = JsonSerializer.Deserialize<SearchQuery>(queryJson, GraphSearch.JsonOptions)!;
        return JsonSerializer.Deserialize<JsonElement>(GraphSearch.RunJson(_db, query));
    }

    private static List<string?> Names(JsonElement result) =>
        result.GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("name").GetString()).ToList();

    [Fact]
    public void ConceptKeywordsFromDocCommentsLeadToPrefabsWithEvidence()
    {
        var result = Run("""
            {"from": {"keywords": ["ボス", "boss"], "types": ["Script"]},
             "steps": [{"direction": "in", "kinds": ["inherits"], "max_hops": 10, "include_self": true},
                       {"direction": "in", "kinds": ["uses_script"], "types": ["Prefab"]}]}
            """);

        var match = Assert.Single(result.GetProperty("matches").EnumerateArray());
        Assert.Equal("Assets/Game/Prefabs/Enemies/Boss.prefab", match.GetProperty("name").GetString());
        Assert.Equal("Assets/Game/Scripts/Boss.cs <-[uses_script]- Assets/Game/Prefabs/Enemies/Boss.prefab",
            match.GetProperty("path").GetString());
        Assert.Equal("Assets/Game/Scripts/Boss.cs", match.GetProperty("from").GetString());
        Assert.Equal(["ボス → doc of Game.Boss", "boss → file name: Boss.cs"],
            match.GetProperty("from_clues").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal("Game.Boss: ボスの行動を制御する", match.GetProperty("from_doc").GetString());
        Assert.False(match.TryGetProperty("clues", out _));
        Assert.Contains("component", result.GetProperty("relations").GetProperty("uses_script").GetString());
    }

    [Fact]
    public void StartWithoutStepsListsRankedCandidatesWithNotes()
    {
        var result = Run("""{"from": {"keywords": ["enemy"]}}""");

        // File name hits outrank the folder hits; Editor/ is annotated.
        Assert.Equal(
            ["Assets/Game/Scripts/Enemy.cs", "Assets/Game/Editor/EnemyInspector.cs",
             "Assets/Game/Prefabs/Enemies/Boss.prefab", "Assets/Game/Prefabs/Enemies/Grunt.prefab"],
            Names(result));
        var inspector = result.GetProperty("matches")[1];
        Assert.Contains("Editor folder", inspector.GetProperty("notes")[0].GetString());
        Assert.Equal(["Game.EnemyInspector"], inspector.GetProperty("declares").EnumerateArray().Select(d => d.GetString()));
    }

    [Fact]
    public void UnreferencedResultsCarryTheResourcesNote()
    {
        var result = Run("""{"from": {"types": ["Prefab"], "source": "Assets"}, "unreferenced": true}""");

        var popup = Assert.Single(result.GetProperty("matches").EnumerateArray());
        Assert.Equal("Assets/Game/Resources/UI/Popup.prefab", popup.GetProperty("name").GetString());
        Assert.Contains("Resources.Load", popup.GetProperty("notes")[0].GetString());
    }

    [Fact]
    public void PathFiltersNarrowStartsAndSteps()
    {
        var result = Run("""
            {"from": {"types": ["Scene"]},
             "steps": [{"direction": "out", "types": ["Prefab"], "path_contains": ["/Enemies/Gr"]}]}
            """);
        Assert.Equal(["Assets/Game/Prefabs/Enemies/Grunt.prefab"], Names(result));
    }

    [Fact]
    public void NothingMatchingRelaxesTheStepsAndSaysHow()
    {
        // Wrong relation (generic) for scene → prefab placement: relaxed to any relation.
        var result = Run("""
            {"from": {"asset": "Grunt.prefab"},
             "steps": [{"direction": "in", "kinds": ["generic"], "types": ["Scene"], "path_contains": ["/Levels/"]}]}
            """);

        Assert.Equal(["Assets/Game/Scenes/Level.unity"], Names(result));
        Assert.Equal(
            ["steps: dropped path_contains", "steps: followed any relation instead of the given kinds"],
            result.GetProperty("relaxed").EnumerateArray().Select(r => r.GetString()));

        var strict = Run("""
            {"from": {"asset": "Grunt.prefab"}, "relax": false,
             "steps": [{"direction": "in", "kinds": ["generic"], "types": ["Scene"]}]}
            """);
        Assert.Equal(0, strict.GetProperty("total_matches").GetInt32());
        Assert.False(strict.TryGetProperty("relaxed", out _));
    }

    [Fact]
    public void ErrorsAndWarnings()
    {
        Assert.Equal("invalid_query", Run("{}").GetProperty("error").GetString());
        Assert.Equal("invalid_start",
            Run("""{"from": {"asset": "Boss.cs", "types": ["Script"]}}""").GetProperty("error").GetString());
        Assert.Equal("not_found", Run("""{"from": {"asset": "Nope.cs"}}""").GetProperty("error").GetString());
        Assert.Equal("invalid_direction",
            Run("""{"from": {"asset": "Boss.cs"}, "steps": [{"direction": "1"}]}""").GetProperty("error").GetString());

        var warned = Run("""{"from": {"asset": "Enemy.cs"}, "relax": false, "steps": [{"direction": "in", "kinds": ["inherit"]}]}""");
        Assert.Contains("'inherit'", warned.GetProperty("warnings")[0].GetString());
    }
}
