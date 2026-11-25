using GuidGraph.Analyzer.Storage;
using GuidGraph.Graph;
using Microsoft.Data.Sqlite;

namespace GuidGraph.Tests;

/// <summary>`analyze --db` deletes the file on a full rebuild, so it must only ever delete
/// graph databases.</summary>
public sealed class AnalyzeDbGuardTests : IDisposable
{
    private readonly TempUnityProject _project = new();
    private readonly string _directory = Directory.CreateTempSubdirectory("guidgraph-guard-").FullName;

    private string DbPath => Path.Combine(_directory, "graph.db");

    public void Dispose()
    {
        _project.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("a text file that is longer than the 100 byte SQLite header, so SQLite reads it and rejects it as a database file.")]
    public void RefusesNonSqliteFile(string content)
    {
        File.WriteAllText(DbPath, content);

        AssertRefused();
    }

    [Theory]
    [InlineData("CREATE TABLE notes (body TEXT)")]
    [InlineData("CREATE TABLE notes (body TEXT); PRAGMA user_version = 1")]
    [InlineData("CREATE TABLE notes (body TEXT); PRAGMA user_version = 3")] // same number as SchemaVersion
    [InlineData("CREATE TABLE assets (a); CREATE TABLE objects (b); CREATE TABLE refs (c); PRAGMA user_version = 3")]
    public void RefusesForeignSqliteDatabase(string sql)
    {
        ExecuteSql(sql);

        AssertRefused();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildsOverMissingOrEmptyFile(bool createEmptyFile)
    {
        if (createEmptyFile)
        {
            File.WriteAllBytes(DbPath, []);
        }

        Assert.Equal(0, Analyze());
        AssertCurrentGraph();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RebuildsOlderGraphDatabase(bool full)
    {
        GraphDatabase.CreateFile(DbPath).Dispose();
        // Schema v2 had no code_symbols table.
        ExecuteSql("DROP TABLE code_symbols; PRAGMA user_version = 2");

        Assert.Equal(0, Analyze(full));
        AssertCurrentGraph();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptsCurrentGraphDatabase(bool full)
    {
        Assert.Equal(0, Analyze());

        Assert.Equal(0, Analyze(full));
        AssertCurrentGraph();
    }

    [Fact]
    public void CreateFileRefusesForeignFile()
    {
        File.WriteAllText(DbPath, "x");

        Assert.Throws<IOException>(() => GraphDatabase.CreateFile(DbPath));
        Assert.Equal("x", File.ReadAllText(DbPath));
    }

    private void AssertRefused()
    {
        var before = File.ReadAllBytes(DbPath);

        Assert.NotEqual(0, Analyze());
        Assert.Equal(before, File.ReadAllBytes(DbPath));
        Assert.NotEqual(0, Analyze(full: true));
        Assert.Equal(before, File.ReadAllBytes(DbPath));
    }

    private void AssertCurrentGraph()
    {
        using var db = GraphDatabase.TryOpenForUpdate(DbPath);
        Assert.NotNull(db);
        Assert.True(db.HasCodeSymbols);
    }

    /// <summary>Runs the real CLI entry point in-process and returns its exit code.</summary>
    private int Analyze(bool full = false)
    {
        string[] args = ["analyze", _project.Root, "--db", DbPath];
        if (full)
        {
            args = [.. args, "--full"];
        }

        return (int)typeof(GraphUpdater).Assembly.EntryPoint!.Invoke(null, [args])!;
    }

    private void ExecuteSql(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=false");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
