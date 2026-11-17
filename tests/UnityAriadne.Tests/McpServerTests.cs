using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using UnityAriadne.Analyzer.Mcp;
using UnityAriadne.Graph;

namespace UnityAriadne.Tests;

/// <summary>
/// End-to-end MCP round trip (Phase 4): the real server wiring (AddMcpServer +
/// WithTools&lt;GraphMcpTools&gt;) talking to the SDK's own client over in-process pipe
/// streams — the same code path as `serve`, minus the stdio endpoints. Uses a small
/// synthetic database file because the tools re-open the file per call.
/// </summary>
public sealed class McpServerTests : IAsyncLifetime
{
    private static readonly string[] ExpectedToolNames =
    [
        "describe_ontology", "find_references", "get_neighbors", "impact_analysis", "list_folders",
        "list_unresolved", "search", "search_nodes",
    ];

    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"ariadne-mcp-test-{Guid.NewGuid():N}.db");

    private IHost? _host;
    private McpClient? _client;

    public async Task InitializeAsync()
    {
        CreateTestDatabase(_dbPath);

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new GraphMcpOptions(_dbPath));
        builder.Services
            .AddMcpServer(options => options.ServerInfo = new() { Name = "unity-ariadne", Version = "test" })
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
            .WithTools<GraphMcpTools>();
        _host = builder.Build();
        await _host.StartAsync();

        _client = await McpClient.CreateAsync(new StreamClientTransport(
            serverInput: clientToServer.Writer.AsStream(),
            serverOutput: serverToClient.Reader.AsStream(),
            loggerFactory: null));
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        File.Delete(_dbPath);
    }

    /// <summary>Same shape as the GraphQueriesTests graph, persisted to a file.</summary>
    private static void CreateTestDatabase(string path)
    {
        using var db = GraphDatabase.CreateFile(path);
        using var writer = db.CreateWriter();
        var scene = writer.AddAsset("aaaa0000000000000000000000000001", "Assets/Scenes/Main.unity", "Scene", "Assets");
        var prefab = writer.AddAsset("aaaa0000000000000000000000000002", "Assets/Prefabs/Hero.prefab", "Prefab", "Assets");
        var material = writer.AddAsset("aaaa0000000000000000000000000003", "Assets/Materials/Sky.mat", "Material", "Assets");
        writer.AddAsset("dead0000000000000000000000000001", null, "Unknown", "Unresolved");

        var sceneObject = writer.AddObject(scene, 1, 1, null, false);
        var prefabObject = writer.AddObject(prefab, 1, 1, null, false);
        writer.AddRef(sceneObject, "aaaa0000000000000000000000000002", 100, "m_SourcePrefab", "prefab_instance");
        writer.AddRef(prefabObject, "aaaa0000000000000000000000000003", 2100000, "m_Materials[0]", "generic");
        writer.AddRef(prefabObject, "dead0000000000000000000000000001", 2100000, "m_Materials[1]", "generic");
        writer.Commit();
        _ = material;
    }

    [Fact]
    public async Task ServerExposesThePlannedTools()
    {
        var tools = await _client!.ListToolsAsync();
        Assert.Equal(ExpectedToolNames, tools.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task FindReferencesRoundTripsOverMcp()
    {
        var result = await CallAsync("find_references", new() { ["name"] = "Sky.mat" });

        Assert.Equal("Assets/Materials/Sky.mat", result.GetProperty("asset").GetProperty("name").GetString());
        Assert.Equal(1, result.GetProperty("total_referrers").GetInt32());
        var reference = result.GetProperty("references")[0];
        Assert.Equal("Assets/Prefabs/Hero.prefab", reference.GetProperty("from").GetString());
        Assert.Equal("generic", reference.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task ImpactAnalysisRoundTripsOverMcp()
    {
        var result = await CallAsync("impact_analysis", new() { ["name"] = "Sky.mat", ["depth"] = 5 });

        Assert.Equal(2, result.GetProperty("total_impacted").GetInt32());
        Assert.Equal(
            ["Assets/Prefabs/Hero.prefab:1", "Assets/Scenes/Main.unity:2"],
            result.GetProperty("impacted").EnumerateArray()
                .Select(i => $"{i.GetProperty("path").GetString()}:{i.GetProperty("distance").GetInt32()}"));
    }

    [Fact]
    public async Task UnknownNameReportsNotFoundInsteadOfFailing()
    {
        var result = await CallAsync("find_references", new() { ["name"] = "Missing.mat" });
        Assert.Equal("not_found", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ListUnresolvedRoundTripsOverMcp()
    {
        var result = await CallAsync("list_unresolved", new());

        Assert.Equal(1, result.GetProperty("total_unresolved").GetInt32());
        var unresolved = result.GetProperty("unresolved")[0];
        Assert.Equal("dead0000000000000000000000000001", unresolved.GetProperty("guid").GetString());
        Assert.Equal(
            ["Assets/Prefabs/Hero.prefab"],
            unresolved.GetProperty("sample_referrers").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task SearchNodesRoundTripsOverMcp()
    {
        var result = await CallAsync("search_nodes", new() { ["query"] = "hero" });
        var match = Assert.Single(result.GetProperty("matches").EnumerateArray());
        Assert.Equal("Assets/Prefabs/Hero.prefab", match.GetProperty("name").GetString());
    }

    [Fact]
    public async Task SearchNodesFiltersByTypeOverMcp()
    {
        var result = await CallAsync("search_nodes", new() { ["type"] = "material" });
        var match = Assert.Single(result.GetProperty("matches").EnumerateArray());
        Assert.Equal("Assets/Materials/Sky.mat", match.GetProperty("name").GetString());

        var unreferenced = await CallAsync("search_nodes", new() { ["unreferenced"] = true, ["source"] = "Assets" });
        Assert.Equal(
            "Assets/Scenes/Main.unity",
            Assert.Single(unreferenced.GetProperty("matches").EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task DescribeOntologyRoundTripsOverMcp()
    {
        var result = await CallAsync("describe_ontology", new());

        var prefab = result.GetProperty("classes").EnumerateArray()
            .Single(c => c.GetProperty("type").GetString() == "Prefab");
        Assert.Equal(1, prefab.GetProperty("count").GetInt32());
        Assert.Equal(1, prefab.GetProperty("by_source").GetProperty("Assets").GetInt32());

        var generic = result.GetProperty("relations").EnumerateArray()
            .Single(r => r.GetProperty("kind").GetString() == "generic");
        Assert.Equal(2, generic.GetProperty("edges").GetInt32());
        Assert.Contains("Prefab->Material: 1",
            generic.GetProperty("signatures").EnumerateArray().Select(s => s.GetString()));
    }

    [Fact]
    public async Task SearchRoundTripsOverMcp()
    {
        var steps = new object[]
        {
            new Dictionary<string, object?> { ["direction"] = "in", ["max_hops"] = 3, ["types"] = new[] { "Scene" } },
        };
        var result = await CallAsync("search", new()
        {
            ["from"] = new Dictionary<string, object?> { ["asset"] = "Sky.mat" },
            ["steps"] = steps,
        });

        Assert.Equal("Assets/Materials/Sky.mat", result.GetProperty("start").GetProperty("assets")[0].GetString());
        var match = Assert.Single(result.GetProperty("matches").EnumerateArray());
        Assert.Equal("Assets/Scenes/Main.unity", match.GetProperty("name").GetString());
        Assert.Equal(
            "Assets/Materials/Sky.mat <-[generic]- Assets/Prefabs/Hero.prefab <-[prefab_instance]- Assets/Scenes/Main.unity",
            match.GetProperty("path").GetString());
        Assert.StartsWith("A -[generic]-> B:",
            result.GetProperty("relations").GetProperty("generic").GetString());
        Assert.False(result.TryGetProperty("relaxed", out _));
    }

    [Fact]
    public async Task SearchByKeywordsAndPathOverMcp()
    {
        var steps = new object[] { new Dictionary<string, object?> { ["direction"] = "out", ["types"] = new[] { "Material" } } };
        var result = await CallAsync("search", new()
        {
            ["from"] = new Dictionary<string, object?> { ["keywords"] = new[] { "hero" }, ["types"] = new[] { "Prefab" } },
            ["steps"] = steps,
        });

        var match = Assert.Single(result.GetProperty("matches").EnumerateArray());
        Assert.Equal("Assets/Materials/Sky.mat", match.GetProperty("name").GetString());
        Assert.Equal("hero → file name: Hero.prefab", match.GetProperty("from_clues")[0].GetString());
    }

    [Fact]
    public async Task SearchRejectsBadArgumentsOverMcp()
    {
        var steps = new object[] { new Dictionary<string, object?> { ["direction"] = "sideways" } };
        var badDirection = await CallAsync("search", new()
        {
            ["from"] = new Dictionary<string, object?> { ["asset"] = "Sky.mat" }, ["steps"] = steps,
        });
        Assert.Equal("invalid_direction", badDirection.GetProperty("error").GetString());

        var noStart = await CallAsync("search", new() { ["from"] = new Dictionary<string, object?>() });
        Assert.Equal("invalid_start", noStart.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ListFoldersRoundTripsOverMcp()
    {
        var folders = await CallAsync("list_folders", new());
        Assert.Equal(
            ["Assets/Materials", "Assets/Prefabs", "Assets/Scenes"],
            folders.GetProperty("folders").EnumerateArray().Select(f => f.GetProperty("path").GetString()));
    }

    [Fact]
    public async Task DescribeOntologyExplainsRelations()
    {
        var result = await CallAsync("describe_ontology", new());
        var instance = result.GetProperty("relations").EnumerateArray()
            .Single(r => r.GetProperty("kind").GetString() == "prefab_instance");
        Assert.True(instance.GetProperty("transitive").GetBoolean());
        Assert.Contains("places", instance.GetProperty("out").GetString());
        Assert.Equal("Placement", result.GetProperty("classes").EnumerateArray()
            .Single(c => c.GetProperty("type").GetString() == "Scene").GetProperty("category").GetString());
    }

    [Fact]
    public async Task SearchFoldsFullWidthKeywordsOverMcp()
    {
        var result = await CallAsync("search", new()
        {
            ["from"] = new Dictionary<string, object?> { ["keywords"] = new[] { "ＨＥＲＯ" } },
        });
        Assert.Equal("ＨＥＲＯ → file name: Hero.prefab",
            Assert.Single(result.GetProperty("matches").EnumerateArray()).GetProperty("clues")[0].GetString());
    }

    private async Task<JsonElement> CallAsync(string tool, Dictionary<string, object?> arguments)
    {
        var result = await _client!.CallToolAsync(tool, arguments);
        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        return JsonSerializer.Deserialize<JsonElement>(text);
    }
}
