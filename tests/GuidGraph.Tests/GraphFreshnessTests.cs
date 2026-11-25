using GuidGraph.Analyzer.Mcp;

namespace GuidGraph.Tests;

public sealed class GraphFreshnessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"guidgraph-fresh-{Guid.NewGuid():N}");
    private readonly string _db;
    private static readonly DateTime Analyzed = new(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc);

    public GraphFreshnessTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Assets"));
        _db = Path.Combine(_root, "graph.db");
        File.WriteAllText(_db, "x");
        File.SetLastWriteTimeUtc(_db, Analyzed);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Touch(string name, DateTime time)
    {
        var path = Path.Combine(_root, "Assets", name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, time);
        return path;
    }

    [Fact]
    public void WithoutProject_OnlyAnalyzedAt()
    {
        var meta = GraphFreshness.Describe(_db, null)!;

        Assert.Equal("2026-11-23T09:00:00Z", meta["analyzed_at"]);
        Assert.False(meta.ContainsKey("stale"));
    }

    [Fact]
    public void MissingProjectFolder_OnlyAnalyzedAt()
    {
        var meta = GraphFreshness.Describe(_db, Path.Combine(_root, "nope"))!;

        Assert.False(meta.ContainsKey("stale"));
    }

    [Fact]
    public void OlderAndIrrelevantFiles_AreNotStale()
    {
        Touch("Old.prefab", Analyzed.AddMinutes(-5));
        Touch("Same.unity", Analyzed.AddMilliseconds(400)); // same second: not newer
        Touch("Newer.cs", Analyzed.AddMinutes(5));          // not a watched extension

        var meta = GraphFreshness.Describe(_db, _root)!;

        Assert.Equal(false, meta["stale"]);
        Assert.False(meta.ContainsKey("stale_count"));
    }

    [Fact]
    public void NewerMetaScenePrefab_CountAsStale()
    {
        Touch("A.prefab", Analyzed.AddMinutes(1));
        Touch("B.unity", Analyzed.AddMinutes(2));
        Touch("C.cs.meta", Analyzed.AddMinutes(3));

        var meta = GraphFreshness.Describe(_db, _root)!;

        Assert.Equal(true, meta["stale"]);
        Assert.Equal(3, meta["stale_count"]);
    }

    [Fact]
    public void MissingDatabase_YieldsNoMeta()
    {
        Assert.Null(GraphFreshness.Describe(Path.Combine(_root, "gone.db"), _root));
    }
}
