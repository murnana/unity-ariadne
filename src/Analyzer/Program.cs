using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnityAriadne.Analyzer.Mcp;
using UnityAriadne.Analyzer.Scanning;
using UnityAriadne.Analyzer.Storage;
using UnityAriadne.Graph;

const int DefaultRetentionDays = 30;

return args switch
{
    ["analyze", .. var rest] => Analyze(rest),
    ["report", .. var rest] => Report(rest),
    ["export", .. var rest] => Export(rest),
    ["serve", .. var rest] => await Serve(rest),
    _ => Usage(),
};

static int Analyze(string[] args)
{
    string? projectPath = null;
    string? dbPath = null;
    string? dataDir = null;
    var retentionDays = DefaultRetentionDays;
    var fullRebuild = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--db" when i + 1 < args.Length:
                dbPath = args[++i];
                break;
            case "--data-dir" when i + 1 < args.Length:
                dataDir = args[++i];
                break;
            case "--retention-days" when i + 1 < args.Length:
                if (!TryParseRetentionDays(args[++i], out retentionDays))
                {
                    return 1;
                }

                break;
            case "--full":
                fullRebuild = true;
                break;
            case var arg when arg.StartsWith('-'):
                Console.Error.WriteLine($"error: unknown option '{arg}'");
                return 1;
            case var arg when projectPath is null:
                projectPath = arg;
                break;
            default:
                Console.Error.WriteLine($"error: unexpected argument '{args[i]}'");
                return 1;
        }
    }

    if (projectPath is null)
    {
        Console.Error.WriteLine("usage: analyze <unity-project-path> [--db <path> | --data-dir <dir>] [--full]");
        return 1;
    }

    if (dbPath is not null && dataDir is not null)
    {
        Console.Error.WriteLine("error: --db and --data-dir cannot be used together");
        return 1;
    }

    if (!Directory.Exists(Path.Combine(projectPath, "Assets")))
    {
        Console.Error.WriteLine($"error: '{projectPath}' does not look like a Unity project (no Assets/ directory)");
        return 1;
    }

    if (dataDir is not null)
    {
        if (ResolveInDataDir(dataDir, projectPath, retentionDays) is not { } resolved)
        {
            return 1;
        }

        dbPath = resolved.DbPath;
        Console.WriteLine($"database: {dbPath}");
    }

    var dictionary = UnityProjectScanner.Scan(projectPath);

    // Phase 4b: an existing database is updated in place (only changed files are
    // re-analyzed); --full forces the old delete-and-rebuild path. A database from
    // another schema version silently falls back to the full rebuild.
    if (dbPath is not null && !fullRebuild
        && GraphDatabase.TryOpenForUpdate(dbPath) is { } existingDb)
    {
        using (existingDb)
        {
            var update = GraphUpdater.Update(projectPath, dictionary, existingDb);
            PrintUpdateSummary(update, existingDb, dbPath);
        }

        return 0;
    }

    // Without --db the same pipeline runs against an in-memory database, so the
    // summary always reflects exactly what a file database would contain.
    using var db = dbPath is null ? GraphDatabase.CreateInMemory() : GraphDatabase.CreateFile(dbPath);
    var summary = GraphBuilder.Build(projectPath, dictionary, db);

    Console.WriteLine($"GUIDs: {dictionary.Count}");
    foreach (var group in dictionary.Entries
                 .GroupBy(e => e.Source)
                 .OrderByDescending(g => g.Count()))
    {
        Console.WriteLine($"  {group.Key}: {group.Count()}");
    }

    Console.WriteLine("Asset types:");
    foreach (var group in dictionary.Entries
                 .GroupBy(e => e.AssetType)
                 .OrderByDescending(g => g.Count())
                 .ThenBy(g => g.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {group.Key}: {group.Count()}");
    }

    Console.WriteLine($"YAML assets: {summary.YamlFileCount} files, {summary.ObjectCount} objects");
    Console.WriteLine(
        $"References: {summary.ExternalRefCount + summary.SameFileRefCount} " +
        $"({summary.ExternalRefCount} external, {summary.SameFileRefCount} same-file; " +
        $"{summary.NullRefCount} null refs skipped)");
    foreach (var (kind, count) in summary.YamlKindCounts
                 .OrderByDescending(p => p.Value)
                 .ThenBy(p => p.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {kind}: {count}");
    }

    Console.WriteLine($"Meta references (meta_external): {summary.MetaRefCount} in {summary.MetaFilesWithRefs} .meta files"
                      + (summary.MetaRegexFallbackCount > 0 ? $" ({summary.MetaRegexFallbackCount} parsed via regex fallback)" : ""));

    Console.WriteLine($"JSON asset references (embedded_json): {summary.JsonRefCount} in {summary.JsonFilesWithRefs} shadergraph files");

    Console.WriteLine(
        $"Code references: {summary.CodeRefCount} from {summary.ScriptObjectCount} of {summary.ScriptFileCount} scripts " +
        $"({summary.DeclaredTypeCount} declared types)");
    foreach (var (kind, count) in summary.CodeKindCounts
                 .OrderByDescending(p => p.Value)
                 .ThenBy(p => p.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {kind}: {count}");
    }

    if (summary.AmbiguousTypeRefCount + summary.SameFileCodeRefCount
        + summary.UnresolvedResourceLoadCount + summary.NonLiteralResourceLoadCount > 0)
    {
        Console.WriteLine(
            $"  skipped: {summary.SameFileCodeRefCount} same-file, {summary.AmbiguousTypeRefCount} ambiguous type names, " +
            $"{summary.UnresolvedResourceLoadCount} unresolved + {summary.NonLiteralResourceLoadCount} non-literal Resources.Load");
    }

    if (summary.UnresolvedAddressablesLoadCount + summary.NonLiteralAddressablesLoadCount > 0)
    {
        Console.WriteLine(
            $"  skipped: {summary.UnresolvedAddressablesLoadCount} unresolved + " +
            $"{summary.NonLiteralAddressablesLoadCount} non-literal Addressables keys");
    }

    Console.WriteLine($"ProjectSettings references: {summary.SettingsRefCount} in {summary.SettingsFileCount} files");
    foreach (var (kind, count) in summary.SettingsKindCounts
                 .OrderByDescending(p => p.Value)
                 .ThenBy(p => p.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {kind}: {count}");
    }

    if (summary.BuiltInGuidCount > 0)
    {
        Console.WriteLine($"Built-in GUIDs resolved: {summary.BuiltInGuidCount}");
    }

    Console.WriteLine($"Unresolved GUIDs: {summary.UnresolvedGuidCount}"
                      + (dbPath is not null && summary.UnresolvedGuidCount > 0 ? $" (details: report --db {dbPath})" : ""));

    if (dictionary.Warnings.Count > 0)
    {
        Console.WriteLine($"Warnings: {dictionary.Warnings.Count}");
        foreach (var warning in dictionary.Warnings.Take(20))
        {
            Console.WriteLine($"  {warning.MetaPath}: {warning.Message}");
        }

        if (dictionary.Warnings.Count > 20)
        {
            Console.WriteLine($"  ... and {dictionary.Warnings.Count - 20} more");
        }
    }

    if (dbPath is not null)
    {
        Console.WriteLine($"Wrote {dbPath} ({summary.TotalRefCount} refs)");
    }

    return 0;
}

static void PrintUpdateSummary(UpdateSummary update, GraphDatabase db, string dbPath)
{
    Console.WriteLine(
        $"Incremental update: {update.AddedCount} added, {update.ChangedCount} changed, " +
        $"{update.MovedCount} moved, {update.RemovedCount} removed, {update.UnchangedCount} unchanged");
    Console.WriteLine($"ProjectSettings: {(update.SettingsReanalyzed ? "re-analyzed" : "unchanged")}");
    Console.WriteLine($"Code layer: {(update.CodeLayerReanalyzed ? "re-analyzed (global resolution)" : "unchanged")}");

    var unresolvedCount = QueryCount(db, "SELECT COUNT(*) FROM unresolved_guids");
    Console.WriteLine($"Unresolved GUIDs: {unresolvedCount}"
                      + (unresolvedCount > 0 ? $" (details: report --db {dbPath})" : ""));
    Console.WriteLine($"Wrote {dbPath} ({QueryCount(db, "SELECT COUNT(*) FROM refs")} refs)"
                      + (update.NothingChanged ? " — no changes detected" : ""));
}

static long QueryCount(GraphDatabase db, string sql)
{
    using var command = db.Connection.CreateCommand();
    command.CommandText = sql;
    return (long)command.ExecuteScalar()!;
}

static int Report(string[] args)
{
    string? dbPath = null;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--db" when i + 1 < args.Length:
                dbPath = args[++i];
                break;
            default:
                Console.Error.WriteLine($"error: unexpected argument '{args[i]}'");
                return 1;
        }
    }

    if (dbPath is null)
    {
        Console.Error.WriteLine("usage: report --db <path>");
        return 1;
    }

    GraphDatabase db;
    try
    {
        db = GraphDatabase.OpenFile(dbPath);
    }
    catch (FileNotFoundException)
    {
        Console.Error.WriteLine($"error: database not found: {dbPath}");
        return 1;
    }

    using (db)
    {
        var unresolved = db.GetUnresolvedGuids();
        if (unresolved.Count == 0)
        {
            Console.WriteLine("No unresolved GUIDs.");
            return 0;
        }

        Console.WriteLine($"Unresolved GUIDs: {unresolved.Count}");
        foreach (var info in unresolved)
        {
            Console.WriteLine($"  {info.Guid}  {info.RefCount} refs from {info.ReferrerCount} assets");
            foreach (var path in info.SampleReferrers)
            {
                Console.WriteLine($"    {path}");
            }

            if (info.ReferrerCount > info.SampleReferrers.Count)
            {
                Console.WriteLine($"    ... and {info.ReferrerCount - info.SampleReferrers.Count} more");
            }
        }

        return 0;
    }
}

static int Export(string[] args)
{
    string? dbPath = null;
    string? jsonlPath = null;
    var includePackages = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--db" when i + 1 < args.Length:
                dbPath = args[++i];
                break;
            case "--jsonl" when i + 1 < args.Length:
                jsonlPath = args[++i];
                break;
            case "--include-packages" or "--include-packages=true":
                includePackages = true;
                break;
            case "--include-packages=false":
                includePackages = false;
                break;
            default:
                Console.Error.WriteLine($"error: unexpected argument '{args[i]}'");
                return 1;
        }
    }

    if (dbPath is null || jsonlPath is null)
    {
        Console.Error.WriteLine("usage: export --db <path> --jsonl <path> [--include-packages]");
        return 1;
    }

    GraphDatabase db;
    try
    {
        db = GraphDatabase.OpenFile(dbPath);
    }
    catch (FileNotFoundException)
    {
        Console.Error.WriteLine($"error: database not found: {dbPath}");
        return 1;
    }

    using (db)
    {
        ExportSummary summary;
        // The MCP memory server splits on plain \n; write UTF-8 without BOM.
        using (var writer = new StreamWriter(jsonlPath, append: false, new UTF8Encoding(false)) { NewLine = "\n" })
        {
            summary = MemoryJsonlExporter.Export(db, writer, new ExportOptions { IncludePackageCache = includePackages });
        }

        Console.WriteLine($"Wrote {jsonlPath} ({summary.EntityCount} entities, {summary.RelationCount} relations)");
        if (summary.SkippedPackageCacheAssetCount > 0)
        {
            Console.WriteLine(
                $"PackageCache assets kept as referenced targets only; {summary.SkippedPackageCacheAssetCount} unreferenced ones omitted (--include-packages exports all)");
        }

        return 0;
    }
}

static async Task<int> Serve(string[] args)
{
    string? dbPath = null;
    string? projectPath = null;
    string? dataDir = null;
    var retentionDays = DefaultRetentionDays;
    var retentionGiven = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--db" when i + 1 < args.Length:
                dbPath = args[++i];
                break;
            case "--project" when i + 1 < args.Length:
                projectPath = args[++i];
                break;
            case "--data-dir" when i + 1 < args.Length:
                dataDir = args[++i];
                break;
            case "--retention-days" when i + 1 < args.Length:
                if (!TryParseRetentionDays(args[++i], out retentionDays))
                {
                    return 1;
                }

                retentionGiven = true;
                break;
            default:
                Console.Error.WriteLine($"error: unexpected argument '{args[i]}'");
                return 1;
        }
    }

    const string usage = "usage: serve (--db <path> | --data-dir <dir> --project <unity-project-path>) [--retention-days <n>]";
    if (dbPath is not null && dataDir is not null)
    {
        Console.Error.WriteLine("error: --db and --data-dir cannot be used together");
        return 1;
    }

    if (dbPath is null && (dataDir is null || projectPath is null))
    {
        Console.Error.WriteLine(usage);
        return 1;
    }

    if (dataDir is null && retentionGiven)
    {
        Console.Error.WriteLine("error: --retention-days needs --data-dir");
        return 1;
    }

    // --data-dir: the database lives in a per-project folder of the data directory, so the
    // location follows from the project path. Keep the project registered while serving.
    ProjectDataStore? store = null;
    if (dataDir is not null)
    {
        if (!Directory.Exists(Path.Combine(projectPath!, "Assets")))
        {
            Console.Error.WriteLine($"error: '{projectPath}' does not look like a Unity project (no Assets/ directory)");
            return 1;
        }

        if (ResolveInDataDir(dataDir, projectPath!, retentionDays) is not { } resolved)
        {
            return 1;
        }

        store = resolved.Store;
        dbPath = resolved.DbPath;
    }

    // --project: build the database first when it does not exist yet, so a plugin
    // can point at ${CLAUDE_PROJECT_DIR} without a manual analyze. An existing
    // database is served as is (refreshing stays an explicit analyze).
    if (projectPath is not null && !File.Exists(dbPath))
    {
        Console.Error.WriteLine($"database not found: {dbPath}; analyzing {projectPath} first");
        // stdout belongs to the stdio transport, but analyze prints its summary there.
        var stdout = Console.Out;
        Console.SetOut(Console.Error);
        try
        {
            if (Analyze([projectPath, "--db", dbPath!]) != 0)
            {
                return 1;
            }
        }
        finally
        {
            Console.SetOut(stdout);
        }
    }

    // Fail fast on a bad path; the tools re-open the file per call afterwards.
    try
    {
        GraphDatabase.OpenFile(dbPath!).Dispose();
    }
    catch (FileNotFoundException)
    {
        Console.Error.WriteLine($"error: database not found: {dbPath}");
        return 1;
    }

    var builder = Host.CreateApplicationBuilder();
    // stdout belongs to the stdio transport — push every log line to stderr.
    builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddSingleton(new GraphMcpOptions(Path.GetFullPath(dbPath!)));
    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "unity-ariadne", Version = "1.0.0" };
            options.ServerInstructions =
                "Asset-level dependency graph of a Unity project (built by unity-ariadne analyze). " +
                "Names accept a project-relative path, a unique file name, or a GUID. " +
                "Re-run analyze to refresh the graph; tools always read the current database file.";
        })
        .WithStdioServerTransport()
        .WithTools<GraphMcpTools>();

    using var keepAlive = new CancellationTokenSource();
    if (store is not null)
    {
        _ = KeepProjectRegistered(store, projectPath!, keepAlive.Token);
    }

    await builder.Build().RunAsync();
    await keepAlive.CancelAsync();
    return 0;
}

// A session can outlive the retention period; refresh the last-used time so another
// session's cleanup never deletes the database this server is reading.
static async Task KeepProjectRegistered(ProjectDataStore store, string projectPath, CancellationToken cancellationToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
    try
    {
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                store.Resolve(projectPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"warning: could not refresh project data: {e.Message}");
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
}

// Registers the project in the data directory, then removes data of projects that were not
// used for retentionDays. Cleanup problems are warnings; a bad data directory is an error.
static (ProjectDataStore Store, string DbPath)? ResolveInDataDir(string dataDir, string projectPath, int retentionDays)
{
    var store = new ProjectDataStore(dataDir);
    string dbPath;
    try
    {
        dbPath = store.Resolve(projectPath);
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
        Console.Error.WriteLine($"error: cannot use data directory '{dataDir}': {e.Message}");
        return null;
    }

    try
    {
        foreach (var removed in store.Prune(TimeSpan.FromDays(retentionDays)))
        {
            Console.Error.WriteLine($"removed unused project data: {removed}");
        }
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"warning: could not clean up '{dataDir}': {e.Message}");
    }

    return (store, dbPath);
}

static bool TryParseRetentionDays(string value, out int days)
{
    if (int.TryParse(value, out days) && days >= 1)
    {
        return true;
    }

    Console.Error.WriteLine($"error: --retention-days must be a whole number of days, 1 or more (got '{value}')");
    return false;
}

static int Usage()
{
    Console.Error.WriteLine(
        """
        Unity Ariadne — Unity project dependency graph analyzer

        usage:
          analyze <unity-project-path> [--db <path> | --data-dir <dir>] [--retention-days <n>] [--full]
              (an existing --db is updated incrementally; --full rebuilds from scratch;
               --data-dir keeps the database in a per-project folder of <dir>)
          report --db <path>
          export --db <path> --jsonl <path> [--include-packages]
          serve --db <path> [--project <unity-project-path>]
          serve --data-dir <dir> --project <unity-project-path> [--retention-days <n>]
              (--project: analyze the project first when the database does not exist;
               --data-dir: per-project database folder, unused ones are removed after <n> days, default 30)
        """);
    return 1;
}
