using Microsoft.Data.Sqlite;
using UnityAriadne.Analyzer.Scanning;
using UnityAriadne.Analyzer.Storage;
using UnityAriadne.Graph;

namespace UnityAriadne.Tests;

/// <summary>
/// A throwaway Unity-project directory assembled from copies of real fixture files
/// (never Claude-authored YAML — mutations are whole-file copies, renames and deletes;
/// only .cs files are ever edited in place).
/// </summary>
public sealed class TempUnityProject : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("ariadne-inc-").FullName;

    public TempUnityProject() => Directory.CreateDirectory(Path.Combine(Root, "Assets"));

    /// <summary>Copies one asset file and its .meta from the fixture, keeping its relative path.</summary>
    public TempUnityProject WithAsset(string relativePath)
    {
        CopyRaw(relativePath);
        CopyRaw(relativePath + ".meta");
        return this;
    }

    /// <summary>Copies a single file (no .meta) from the fixture — for ProjectSettings.</summary>
    public TempUnityProject WithFile(string relativePath)
    {
        CopyRaw(relativePath);
        return this;
    }

    /// <summary>Copies a whole directory tree from an arbitrary source root (e.g. the
    /// fixtures/Addressables fixture) under a project-relative target directory.</summary>
    public TempUnityProject WithTreeFrom(string sourceRoot, string targetRelative)
    {
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Root, targetRelative, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return this;
    }

    /// <summary>Replaces an asset's content with another fixture file's bytes (guid — in the
    /// untouched .meta — stays the same, so this simulates editing the asset in Unity).</summary>
    public void OverwriteAssetWith(string relativePath, string fixtureRelativePath) =>
        File.Copy(
            Path.Combine(Fixtures.SampleProject, fixtureRelativePath),
            Path.Combine(Root, relativePath),
            overwrite: true);

    public void DeleteAsset(string relativePath)
    {
        File.Delete(Path.Combine(Root, relativePath));
        File.Delete(Path.Combine(Root, relativePath + ".meta"));
    }

    public void DeleteFile(string relativePath) => File.Delete(Path.Combine(Root, relativePath));

    public void RenameAsset(string fromRelativePath, string toRelativePath)
    {
        File.Move(Path.Combine(Root, fromRelativePath), Path.Combine(Root, toRelativePath));
        File.Move(Path.Combine(Root, fromRelativePath + ".meta"), Path.Combine(Root, toRelativePath + ".meta"));
    }

    /// <summary>Appends a line to a project file — used only on .cs sources.</summary>
    public void AppendLine(string relativePath, string line) =>
        File.AppendAllText(Path.Combine(Root, relativePath), "\n" + line + "\n");

    private void CopyRaw(string relativePath)
    {
        var target = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(Path.Combine(Fixtures.SampleProject, relativePath), target);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

/// <summary>
/// Phase 4b: the incremental updater's invariant is that after any sequence of file
/// changes, Update leaves the database row-for-row identical to a fresh full Build of
/// the same tree (modulo rowids — the dumps below join them away).
/// </summary>
public class GraphUpdaterTests
{
    private const string Prefab = "Assets/Art/Prefabs/Town Wall Border/SM_Town_Wall_Border_12.prefab";
    private const string Fbx = "Assets/Art/Meshes/Town Wall Border/SM_Town_Wall_Border_12.fbx";
    private const string Material = "Assets/Art/Fog/Materials/CubeFogLevel2.mat";
    private const string OtherMaterial = "Assets/Art/Fog/Materials/CubeFogLevel3.mat";
    private const string Script = "Assets/Scripts/InputProviders/InputProvider.cs";
    private const string InterfaceScript = "Assets/Scripts/Interfaces/IInputProvider.cs";

    /// <summary>Prefab → FBX (YAML ref), FBX .meta → material remap (meta_external, dangling
    /// here), a material, one script (its IInputProvider base is unresolvable at first), and
    /// two ProjectSettings files (EditorBuildSettings scene guids dangle — fine).</summary>
    private static TempUnityProject CreateBaseProject() =>
        new TempUnityProject()
            .WithAsset(Prefab)
            .WithAsset(Fbx)
            .WithAsset(Material)
            .WithAsset(Script)
            .WithFile("ProjectSettings/EditorBuildSettings.asset")
            .WithFile("ProjectSettings/TimeManager.asset");

    [FixtureFact]
    public void NoChange_LeavesDatabaseUntouched()
    {
        using var project = CreateBaseProject();
        var update = AssertUpdateMatchesFullRebuild(project, _ => { });
        Assert.True(update.NothingChanged);
        Assert.Equal(0, update.AddedCount + update.ChangedCount + update.MovedCount + update.RemovedCount);
        Assert.True(update.UnchangedCount > 0);
    }

    [FixtureFact]
    public void ModifiedAsset_IsReanalyzed()
    {
        using var project = CreateBaseProject();
        var update = AssertUpdateMatchesFullRebuild(
            project, p => p.OverwriteAssetWith(Material, OtherMaterial));
        Assert.Equal(1, update.ChangedCount);
        Assert.Equal(0, update.AddedCount + update.MovedCount + update.RemovedCount);
        Assert.False(update.CodeLayerReanalyzed);
        Assert.False(update.SettingsReanalyzed);
    }

    [FixtureFact]
    public void AddedAsset_IsAnalyzed()
    {
        using var project = CreateBaseProject();
        var update = AssertUpdateMatchesFullRebuild(
            project, p => p.WithAsset(OtherMaterial));
        Assert.Equal(1, update.AddedCount);
        // Any change to the asset-path set re-resolves the code layer (Resources.Load
        // matches by path, new types can appear).
        Assert.True(update.CodeLayerReanalyzed);
    }

    [FixtureFact]
    public void RemovedReferencedAsset_BecomesUnresolved()
    {
        using var project = CreateBaseProject();
        using var db = GraphDatabase.CreateInMemory();
        GraphBuilder.Build(project.Root, UnityProjectScanner.Scan(project.Root), db);
        var fbxGuid = QueryStrings(db, "SELECT guid FROM assets WHERE path LIKE '%.fbx'").Single();

        project.DeleteAsset(Fbx);
        var update = GraphUpdater.Update(project.Root, UnityProjectScanner.Scan(project.Root), db);

        Assert.Equal(1, update.RemovedCount);
        // The prefab still references the FBX guid, so the row survives as Unresolved.
        Assert.Equal(
            ["Unknown|Unresolved"],
            QueryStrings(db, $"SELECT asset_type || '|' || source FROM assets WHERE guid = '{fbxGuid}'"));
        AssertMatchesFullRebuild(project, db);
    }

    [FixtureFact]
    public void RemovedUnreferencedAsset_RowIsDropped()
    {
        using var project = CreateBaseProject();
        using var db = GraphDatabase.CreateInMemory();
        GraphBuilder.Build(project.Root, UnityProjectScanner.Scan(project.Root), db);
        var materialGuid = QueryStrings(db, "SELECT guid FROM assets WHERE path LIKE '%CubeFogLevel2.mat'").Single();

        project.DeleteAsset(Material);
        var update = GraphUpdater.Update(project.Root, UnityProjectScanner.Scan(project.Root), db);

        Assert.Equal(1, update.RemovedCount);
        // Nothing referenced the material, so no Unresolved row survives for it.
        Assert.Empty(QueryStrings(db, $"SELECT guid FROM assets WHERE guid = '{materialGuid}'"));
        AssertMatchesFullRebuild(project, db);
    }

    [FixtureFact]
    public void RenamedAsset_UpdatesRowWithoutReanalysis()
    {
        using var project = CreateBaseProject();
        var update = AssertUpdateMatchesFullRebuild(
            project, p => p.RenameAsset(Material, "Assets/Art/Fog/Materials/CubeFogRenamed.mat"));
        Assert.Equal(1, update.MovedCount);
        Assert.Equal(0, update.ChangedCount);
        Assert.True(update.CodeLayerReanalyzed);
    }

    [FixtureFact]
    public void ModifiedScript_RerunsCodeLayer()
    {
        using var project = CreateBaseProject();
        var update = AssertUpdateMatchesFullRebuild(
            project, p => p.AppendLine(Script, "// touched by incremental-update test"));
        Assert.Equal(1, update.ChangedCount);
        Assert.True(update.CodeLayerReanalyzed);
    }

    /// <summary>
    /// The key global-invalidation case: adding IInputProvider.cs gives the untouched
    /// InputProvider.cs a new inherits edge. A naive per-file updater would miss it.
    /// </summary>
    [FixtureFact]
    public void AddedScript_ResolvesRefsOfUnchangedScript()
    {
        using var project = CreateBaseProject();
        using var db = GraphDatabase.CreateInMemory();
        GraphBuilder.Build(project.Root, UnityProjectScanner.Scan(project.Root), db);
        Assert.Empty(QueryStrings(db, "SELECT ref_kind FROM refs WHERE ref_kind = 'inherits'"));

        project.WithAsset(InterfaceScript);
        var update = GraphUpdater.Update(project.Root, UnityProjectScanner.Scan(project.Root), db);

        Assert.True(update.CodeLayerReanalyzed);
        Assert.NotEmpty(QueryStrings(db, "SELECT ref_kind FROM refs WHERE ref_kind = 'inherits'"));
        AssertMatchesFullRebuild(project, db);
    }

    [FixtureFact]
    public void UnresolvedGuid_BecomesResolvedWhenAssetAppears()
    {
        using var project = new TempUnityProject()
            .WithAsset(Prefab)  // references the FBX guid, which does not exist yet
            .WithAsset(Script);
        using var db = GraphDatabase.CreateInMemory();
        GraphBuilder.Build(project.Root, UnityProjectScanner.Scan(project.Root), db);
        Assert.Single(QueryStrings(db, "SELECT a.guid FROM assets a JOIN unresolved_guids u ON u.guid = a.guid WHERE a.asset_type = 'Unknown'"));

        project.WithAsset(Fbx);
        var update = GraphUpdater.Update(project.Root, UnityProjectScanner.Scan(project.Root), db);

        Assert.Equal(1, update.AddedCount);
        Assert.NotEmpty(QueryStrings(db, "SELECT path FROM assets WHERE path LIKE '%.fbx'"));
        AssertMatchesFullRebuild(project, db);
    }

    [FixtureFact]
    public void RemovedSettingsFile_ReanalyzesSettingsLayer()
    {
        using var project = CreateBaseProject();
        var update = AssertUpdateMatchesFullRebuild(
            project, p => p.DeleteFile("ProjectSettings/TimeManager.asset"));
        Assert.True(update.SettingsReanalyzed);
        Assert.False(update.CodeLayerReanalyzed);
    }

    [FixtureFact]
    public void AddedSettingsFile_ReanalyzesSettingsLayer()
    {
        using var project = CreateBaseProject();
        var update = AssertUpdateMatchesFullRebuild(
            project, p => p.WithFile("ProjectSettings/GraphicsSettings.asset"));
        Assert.True(update.SettingsReanalyzed);
    }

    [Fact]
    public void TryOpenForUpdate_RejectsOtherSchemaVersions()
    {
        var dbPath = Path.Combine(Directory.CreateTempSubdirectory("ariadne-ver-").FullName, "graph.db");
        try
        {
            GraphDatabase.CreateFile(dbPath).Dispose();

            using (var db = GraphDatabase.TryOpenForUpdate(dbPath))
            {
                Assert.NotNull(db);
            }

            using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=false"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version = 1";
                command.ExecuteNonQuery();
            }

            Assert.Null(GraphDatabase.TryOpenForUpdate(dbPath));
            Assert.Null(GraphDatabase.TryOpenForUpdate(dbPath + ".missing"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dbPath)!, recursive: true);
        }
    }

    /// <summary>Runs full build → mutate → incremental update, and asserts the database
    /// equals a fresh full build of the mutated tree.</summary>
    private static UpdateSummary AssertUpdateMatchesFullRebuild(
        TempUnityProject project, Action<TempUnityProject> mutate)
    {
        using var db = GraphDatabase.CreateInMemory();
        GraphBuilder.Build(project.Root, UnityProjectScanner.Scan(project.Root), db);

        mutate(project);
        var update = GraphUpdater.Update(project.Root, UnityProjectScanner.Scan(project.Root), db);

        AssertMatchesFullRebuild(project, db);
        return update;
    }

    internal static void AssertMatchesFullRebuild(TempUnityProject project, GraphDatabase updated)
    {
        using var fresh = GraphDatabase.CreateInMemory();
        GraphBuilder.Build(project.Root, UnityProjectScanner.Scan(project.Root), fresh);
        Assert.Equal(Dump(fresh), Dump(updated));
    }

    /// <summary>Canonical row-id-independent dump of every table, sorted.</summary>
    internal static List<string> Dump(GraphDatabase db)
    {
        var rows = new List<string>();
        rows.AddRange(QueryStrings(db,
            """
            SELECT 'asset|' || guid || '|' || COALESCE(path, '-') || '|' || asset_type
                   || '|' || source || '|' || COALESCE(file_hash, '-')
            FROM assets ORDER BY 1
            """));
        rows.AddRange(QueryStrings(db,
            """
            SELECT 'object|' || a.guid || '|' || o.file_id || '|' || o.class_id
                   || '|' || COALESCE(o.name, '-') || '|' || o.is_stripped
            FROM objects o JOIN assets a ON a.id = o.asset_id ORDER BY 1
            """));
        rows.AddRange(QueryStrings(db,
            """
            SELECT 'ref|' || a.guid || '|' || o.file_id || '|' || COALESCE(r.to_guid, '-')
                   || '|' || r.to_file_id || '|' || r.property_path || '|' || r.ref_kind
            FROM refs r JOIN objects o ON o.id = r.from_object_id JOIN assets a ON a.id = o.asset_id
            ORDER BY 1
            """));
        rows.AddRange(QueryStrings(db,
            "SELECT 'settings_file|' || path || '|' || file_hash FROM settings_files ORDER BY 1"));
        return rows;
    }

    private static List<string> QueryStrings(GraphDatabase db, string sql)
    {
        var results = new List<string>();
        using var command = db.Connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }
}

/// <summary>
/// The same no-change invariant at fixture scale: one extra full analysis, then an
/// incremental update over the untouched fixture must detect zero changes and leave
/// every row byte-identical.
/// </summary>
public class GraphUpdaterFixtureTests
{
    [FixtureFact]
    public void FixtureNoChangeUpdate_DetectsNothingAndKeepsAllRows()
    {
        using var db = GraphDatabase.CreateInMemory();
        var dictionary = UnityProjectScanner.Scan(Fixtures.SampleProject);
        GraphBuilder.Build(Fixtures.SampleProject, dictionary, db);
        var before = GraphUpdaterTests.Dump(db);

        var update = GraphUpdater.Update(Fixtures.SampleProject, dictionary, db);

        Assert.True(update.NothingChanged);
        Assert.Equal(dictionary.Count, update.UnchangedCount);
        Assert.Equal(before, GraphUpdaterTests.Dump(db));
    }
}
