using System.Collections.Concurrent;

namespace GuidGraph.Analyzer.Mcp;

/// <summary>
/// How current the database is: its own write time (the time `analyze` finished) against the
/// modification times of the project's .meta/.unity/.prefab files. File times only — no VCS,
/// no file contents. Deleted files are not detected. Comparison is per second, so filesystem
/// timestamp precision does not cause false positives.
/// </summary>
public static class GraphFreshness
{
    private const int MaxStaleCount = 1000;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly string[] WatchedExtensions = [".meta", ".unity", ".prefab"];
    private static readonly ConcurrentDictionary<(string Project, long Analyzed), (DateTime At, int Count)> Cache = new();

    /// <summary>
    /// The `meta` object of a tool answer, or null when the database file is unreadable. `stale` and
    /// `stale_count` appear only when the project folder is known and exists.
    /// </summary>
    public static Dictionary<string, object>? Describe(string dbPath, string? projectPath)
    {
        if (!File.Exists(dbPath))
        {
            return null;
        }

        var analyzed = Truncate(File.GetLastWriteTimeUtc(dbPath));

        var meta = new Dictionary<string, object> { ["analyzed_at"] = analyzed.ToString("yyyy-MM-ddTHH:mm:ssZ") };
        var assets = projectPath is null ? null : Path.Combine(projectPath, "Assets");
        if (assets is not null && Directory.Exists(assets))
        {
            var count = CountNewerFiles(assets, analyzed);
            meta["stale"] = count > 0;
            if (count > 0)
            {
                meta["stale_count"] = count;
            }
        }

        return meta;
    }

    private static int CountNewerFiles(string assetsDirectory, DateTime analyzed)
    {
        var key = (assetsDirectory, analyzed.Ticks);
        if (Cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < CacheTtl)
        {
            return cached.Count;
        }

        var count = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var path in Directory.EnumerateFiles(assetsDirectory, "*", options))
        {
            if (!WatchedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Truncate(File.GetLastWriteTimeUtc(path)) > analyzed && ++count >= MaxStaleCount)
            {
                break;
            }
        }

        Cache[key] = (DateTime.UtcNow, count);
        return count;
    }

    private static DateTime Truncate(DateTime time) => new(time.Ticks - time.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}
