using System.Text.Json;
using System.Text.RegularExpressions;
using UnityAriadne.Analyzer.Storage;

namespace UnityAriadne.Tests;

public sealed partial class ProjectDataStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ariadne-data-test-{Guid.NewGuid():N}");
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private string DataDir => Path.Combine(_root, "data");

    private ProjectDataStore NewStore(Func<string, string>? hash = null) => new(DataDir, _clock, hash);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_PutsTheDatabaseInAHashFolderOfTheDataDir()
    {
        var dbPath = NewStore().Resolve(Path.Combine(_root, "MyGame"));

        Assert.Equal("graph.db", Path.GetFileName(dbPath));
        var folder = Path.GetFileName(Path.GetDirectoryName(dbPath))!;
        Assert.Matches(HexFolder(), folder);
        Assert.Equal(DataDir, Path.GetDirectoryName(Path.GetDirectoryName(dbPath)));
        Assert.True(Directory.Exists(Path.GetDirectoryName(dbPath)));
    }

    [Fact]
    public void Resolve_IsStableForTheSameProjectAndDiffersBetweenProjects()
    {
        var store = NewStore();
        var a = store.Resolve(Path.Combine(_root, "A"));

        Assert.Equal(a, store.Resolve(Path.Combine(_root, "A")));
        Assert.Equal(a, NewStore().Resolve(Path.Combine(_root, "A")));
        Assert.NotEqual(a, store.Resolve(Path.Combine(_root, "B")));
    }

    [Fact]
    public void Resolve_IgnoresTrailingSeparatorsAndDotSegments()
    {
        var store = NewStore();
        var expected = store.Resolve(Path.Combine(_root, "MyGame"));

        Assert.Equal(expected, store.Resolve(Path.Combine(_root, "MyGame") + Path.DirectorySeparatorChar));
        Assert.Equal(expected, store.Resolve(Path.Combine(_root, "Other", "..", "MyGame")));
    }

    [Fact]
    public void Resolve_RecordsNameAndPathInTheIndex()
    {
        var store = NewStore();
        var project = Path.Combine(_root, "MyGame");
        var folder = Path.GetFileName(Path.GetDirectoryName(store.Resolve(project)))!;

        var entry = Assert.Single(store.ReadEntries());
        Assert.Equal(folder, entry.Key);
        Assert.Equal("MyGame", entry.Value.Name);
        Assert.Equal(project, entry.Value.Path);
        Assert.Equal(_clock.GetUtcNow(), entry.Value.LastUsed);

        // The index is a plain JSON file next to the project folders.
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDir, "index.json")));
        Assert.Equal("MyGame", json.RootElement.GetProperty("entries").GetProperty(folder).GetProperty("name").GetString());
    }

    [Fact]
    public void Resolve_RefreshesLastUsed()
    {
        var store = NewStore();
        store.Resolve(Path.Combine(_root, "A"));
        _clock.Advance(TimeSpan.FromDays(3));
        store.Resolve(Path.Combine(_root, "A"));

        Assert.Equal(_clock.GetUtcNow(), Assert.Single(store.ReadEntries()).Value.LastUsed);
    }

    [Fact]
    public void Resolve_PrefixCollisionGetsALongerFolderNameAndEachProjectKeepsItsOwn()
    {
        // Every path hashes to the same digest, so the shortest prefixes collide.
        var store = NewStore(_ => new string('a', 64));
        var a = store.Resolve(Path.Combine(_root, "A"));
        var b = store.Resolve(Path.Combine(_root, "B"));

        Assert.NotEqual(a, b);
        Assert.Equal(12, Path.GetFileName(Path.GetDirectoryName(a))!.Length);
        Assert.Equal(16, Path.GetFileName(Path.GetDirectoryName(b))!.Length);
        Assert.Equal(a, store.Resolve(Path.Combine(_root, "A")));
        Assert.Equal(b, store.Resolve(Path.Combine(_root, "B")));
        Assert.Equal(2, store.ReadEntries().Count);
    }

    [Fact]
    public void Resolve_AProjectKeepsItsFolderWhenAShorterNameBecomesFreeAgain()
    {
        var store = NewStore(_ => new string('a', 64));
        store.Resolve(Path.Combine(_root, "A"));
        _clock.Advance(TimeSpan.FromDays(20));
        var b = store.Resolve(Path.Combine(_root, "B"));

        store.Prune(TimeSpan.FromDays(15)); // A is gone, its 12-character name is free

        Assert.Equal(b, store.Resolve(Path.Combine(_root, "B")));
        Assert.Single(store.ReadEntries());
    }

    [Fact]
    public void Prune_RemovesProjectsUnusedForLongerThanTheRetentionAndKeepsTheRest()
    {
        var store = NewStore();
        var old = store.Resolve(Path.Combine(_root, "Old"));
        _clock.Advance(TimeSpan.FromDays(25));
        var recent = store.Resolve(Path.Combine(_root, "Recent"));
        File.WriteAllText(old, "x");
        File.WriteAllText(recent, "x");
        _clock.Advance(TimeSpan.FromDays(10)); // Old: 35 days, Recent: 10 days

        var removed = store.Prune(TimeSpan.FromDays(30));

        Assert.Equal([Path.GetFileName(Path.GetDirectoryName(old))!], removed);
        Assert.False(Directory.Exists(Path.GetDirectoryName(old)));
        Assert.True(File.Exists(recent));
        Assert.Equal("Recent", Assert.Single(store.ReadEntries()).Value.Name);
    }

    [Fact]
    public void Prune_KeepsEverythingWithinTheRetention()
    {
        var store = NewStore();
        var db = store.Resolve(Path.Combine(_root, "A"));
        File.WriteAllText(db, "x");
        _clock.Advance(TimeSpan.FromDays(29));

        Assert.Empty(store.Prune(TimeSpan.FromDays(30)));
        Assert.True(File.Exists(db));
    }

    [Fact]
    public void Prune_RemovesOldHashFoldersTheIndexDoesNotKnow_ButNothingElse()
    {
        // Folder timestamps come from the real file system, so run this test on real time.
        _clock.Set(DateTimeOffset.UtcNow);
        var store = NewStore();
        store.Resolve(Path.Combine(_root, "A"));
        var orphan = Directory.CreateDirectory(Path.Combine(DataDir, "0123456789abcdef"));
        File.WriteAllText(Path.Combine(orphan.FullName, "graph.db"), "x");
        var foreign = Directory.CreateDirectory(Path.Combine(DataDir, "notes"));
        File.WriteAllText(Path.Combine(foreign.FullName, "keep.txt"), "x");

        // Recent orphan: kept.
        Assert.Empty(store.Prune(TimeSpan.FromDays(30)));
        Assert.True(orphan.Exists);

        // Old orphan: removed. A folder that does not look like ours is never touched.
        _clock.Set(DateTimeOffset.UtcNow.AddDays(60));
        store.Prune(TimeSpan.FromDays(30));
        Assert.False(Directory.Exists(orphan.FullName));
        Assert.True(File.Exists(Path.Combine(foreign.FullName, "keep.txt")));
    }

    [Fact]
    public void Prune_NeverDeletesOutsideTheDataDirEvenWithATamperedIndex()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside"));
        File.WriteAllText(Path.Combine(outside.FullName, "keep.txt"), "x");
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(
            Path.Combine(DataDir, "index.json"),
            """{"version":1,"entries":{"../outside":{"name":"x","path":"/x","lastUsed":"2000-01-01T00:00:00Z"}}}""");

        var store = NewStore();
        Assert.Empty(store.Prune(TimeSpan.FromDays(30)));

        Assert.True(File.Exists(Path.Combine(outside.FullName, "keep.txt")));
        Assert.Empty(store.ReadEntries());
    }

    [Fact]
    public void Resolve_RecoversFromACorruptIndex()
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(Path.Combine(DataDir, "index.json"), "{ not json");

        var store = NewStore();
        var db = store.Resolve(Path.Combine(_root, "A"));

        Assert.Equal(db, store.Resolve(Path.Combine(_root, "A")));
        Assert.Single(store.ReadEntries());
        Assert.True(File.Exists(Path.Combine(DataDir, "index.json.corrupt")));
    }

    [Fact]
    public void Resolve_ConcurrentCallsDoNotLoseEntries()
    {
        const int count = 24;
        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            NewStore().Resolve(Path.Combine(_root, $"Project{i}")));

        var entries = NewStore().ReadEntries();
        Assert.Equal(count, entries.Count);
        Assert.Equal(count, entries.Values.Select(e => e.Name).Distinct().Count());
    }

    [GeneratedRegex("^[0-9a-f]{12}$")]
    private static partial Regex HexFolder();

    private sealed class TestClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;

        public void Set(DateTimeOffset now) => _now = now;
    }
}
