using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnityAriadne.Analyzer.Storage;

/// <summary>
/// Keeps one graph database per Unity project under a single data directory (the Claude Code
/// plugin data directory). Each project gets <c>&lt;data-dir&gt;/&lt;hash&gt;/graph.db</c>, where
/// the hash is derived from the project path, and <c>&lt;data-dir&gt;/index.json</c> records which
/// project each hash belongs to. Projects that were not used for a while are pruned.
/// </summary>
internal sealed partial class ProjectDataStore
{
    public const string DatabaseFileName = "graph.db";
    public const string IndexFileName = "index.json";
    public const string LockFileName = "index.lock";

    private const int MinHashLength = 12;
    private const int MaxHashLength = 64;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _dataDir;
    private readonly TimeProvider _clock;
    private readonly Func<string, string> _hash;

    /// <param name="dataDir">Directory that holds the index and the per-project folders.</param>
    /// <param name="clock">Test seam for "now".</param>
    /// <param name="hash">Test seam: maps a normalized path to a lower-case hex digest (64 chars).</param>
    public ProjectDataStore(string dataDir, TimeProvider? clock = null, Func<string, string>? hash = null)
    {
        _dataDir = Path.GetFullPath(dataDir);
        _clock = clock ?? TimeProvider.System;
        _hash = hash ?? Sha256Hex;
    }

    /// <summary>
    /// Returns the database path for <paramref name="projectPath"/>, registering the project in
    /// the index (or refreshing its last-used time) and creating its folder.
    /// </summary>
    public string Resolve(string projectPath)
    {
        var path = NormalizePath(projectPath);
        var key = WithIndex(index =>
        {
            // A project that is already registered keeps its folder, even if a shorter
            // hash became free again after another project was pruned.
            var existing = index.Entries.FirstOrDefault(e => PathEquals(e.Value.Path, path));
            var key = existing.Key ?? FreeKey(index, _hash(HashInput(path)));

            index.Entries[key] = new IndexEntry
            {
                Name = ProjectName(path),
                Path = path,
                LastUsed = _clock.GetUtcNow(),
            };
            return key;
        });

        var dir = Path.Combine(_dataDir, key);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, DatabaseFileName);
    }

    /// <summary>
    /// Deletes projects whose last use is older than <paramref name="retention"/>, together
    /// with hash-named folders the index does not know about (an index that was lost) once
    /// their files are that old. A folder that cannot be deleted stays registered and is
    /// retried on the next run. Returns the removed folder names.
    /// </summary>
    public IReadOnlyList<string> Prune(TimeSpan retention)
    {
        var cutoff = _clock.GetUtcNow() - retention;
        var removed = new List<string>();

        WithIndex(index =>
        {
            foreach (var (key, entry) in index.Entries.ToList())
            {
                if (entry.LastUsed >= cutoff)
                {
                    continue;
                }

                // Never trust a key read from disk as a path segment.
                if (!HashFolderName().IsMatch(key))
                {
                    index.Entries.Remove(key);
                    continue;
                }

                if (TryDeleteFolder(key))
                {
                    index.Entries.Remove(key);
                    removed.Add(key);
                }
            }

            if (Directory.Exists(_dataDir))
            {
                foreach (var dir in Directory.EnumerateDirectories(_dataDir))
                {
                    var name = Path.GetFileName(dir);
                    if (!HashFolderName().IsMatch(name)
                        || index.Entries.ContainsKey(name)
                        || new DateTimeOffset(LatestWriteUtc(dir), TimeSpan.Zero) >= cutoff)
                    {
                        continue;
                    }

                    if (TryDeleteFolder(name))
                    {
                        removed.Add(name);
                    }
                }
            }

            return 0;
        });

        return removed;
    }

    /// <summary>The registered projects (for tests and diagnostics).</summary>
    public IReadOnlyDictionary<string, IndexEntry> ReadEntries() =>
        WithIndex(index => (IReadOnlyDictionary<string, IndexEntry>)new Dictionary<string, IndexEntry>(index.Entries));

    internal static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return full.Length > (root?.Length ?? 0)
            ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : full;
    }

    private static string FreeKey(IndexFile index, string digest)
    {
        // Shortest prefix that is not taken by another project. A prefix collision between two
        // different projects makes the later one use a longer name.
        for (var length = MinHashLength; length <= MaxHashLength && length <= digest.Length; length += 4)
        {
            var candidate = digest[..length];
            if (!index.Entries.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("could not find a free folder name for the project");
    }

    private static string HashInput(string normalizedPath) =>
        OperatingSystem.IsWindows() ? normalizedPath.ToLowerInvariant() : normalizedPath;

    private static bool PathEquals(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string ProjectName(string normalizedPath)
    {
        var name = Path.GetFileName(normalizedPath);
        return name.Length > 0 ? name : normalizedPath;
    }

    private static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static DateTime LatestWriteUtc(string dir)
    {
        var latest = Directory.GetLastWriteTimeUtc(dir);
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var time = File.GetLastWriteTimeUtc(file);
            if (time > latest)
            {
                latest = time;
            }
        }

        return latest;
    }

    private bool TryDeleteFolder(string name)
    {
        var dir = Path.Combine(_dataDir, name);
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"warning: could not delete {dir}: {e.Message}");
            return false;
        }
    }

    // Several Claude Code sessions can start at once, so every read-modify-write of the
    // index happens under an exclusive lock file and is committed with an atomic rename.
    private T WithIndex<T>(Func<IndexFile, T> action)
    {
        Directory.CreateDirectory(_dataDir);
        using var lockFile = AcquireLock();

        var index = ReadIndex();
        var result = action(index);
        WriteIndex(index);
        return result;
    }

    private FileStream AcquireLock()
    {
        var path = Path.Combine(_dataDir, LockFileName);
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
        }
    }

    private IndexFile ReadIndex()
    {
        var path = Path.Combine(_dataDir, IndexFileName);
        if (!File.Exists(path))
        {
            return new IndexFile();
        }

        try
        {
            var index = JsonSerializer.Deserialize<IndexFile>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
            if (index is not null)
            {
                index.Entries = new Dictionary<string, IndexEntry>(index.Entries, StringComparer.Ordinal);
                return index;
            }
        }
        catch (JsonException)
        {
            // Fall through: keep the broken file for inspection and start over. Project folders
            // that lose their entry this way are cleaned up by Prune once they are old.
        }

        File.Move(path, path + ".corrupt", overwrite: true);
        return new IndexFile();
    }

    private void WriteIndex(IndexFile index)
    {
        var path = Path.Combine(_dataDir, IndexFileName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(index, JsonOptions), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    // Folder names this class creates: a lower-case hex prefix of the path hash.
    [GeneratedRegex("^[0-9a-f]{12,64}$")]
    private static partial Regex HashFolderName();

    internal sealed class IndexFile
    {
        public int Version { get; set; } = 1;

        public Dictionary<string, IndexEntry> Entries { get; set; } = new(StringComparer.Ordinal);
    }

    internal sealed class IndexEntry
    {
        public string Name { get; set; } = "";

        public string Path { get; set; } = "";

        public DateTimeOffset LastUsed { get; set; }
    }
}
