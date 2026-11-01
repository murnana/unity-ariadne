using System.Buffers.Binary;
using System.Security.Cryptography;
using UnityAriadne.Analyzer.Scanning;
using UnityAriadne.Analyzer.Yaml;

namespace UnityAriadne.Analyzer.Storage;

/// <summary>
/// Computes the per-asset change-detection hash stored in assets.file_hash (Phase 4b).
/// The hash covers exactly the bytes the analysis reads: the .meta file always, plus the
/// asset file itself only when it is a parse candidate (Unity-YAML by header sniff — which
/// includes .vfx JSON-in-YAML — a .shadergraph/.shadersubgraph JSON file, or a .cs source).
/// Binary assets (textures, FBX, ...) contribute refs only through their .meta, so their
/// content changes are irrelevant and never read — the 139 MB fixture .tif stays unread.
/// </summary>
public static class ContentHasher
{
    public static string HashAsset(string projectRoot, AssetEntry entry)
    {
        var fullPath = Path.Combine(projectRoot, entry.Path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFile(hash, IsParseCandidate(entry, fullPath) ? fullPath : null);
        AppendFile(hash, fullPath + ".meta");
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Hash of a single file (ProjectSettings change detection).</summary>
    public static string HashFile(string path)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFile(hash, path);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static bool IsParseCandidate(AssetEntry entry, string fullPath)
    {
        if (entry.AssetType == "Folder" || !File.Exists(fullPath))
        {
            return false;
        }

        return fullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
               || fullPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase)
               || fullPath.EndsWith(".shadersubgraph", StringComparison.OrdinalIgnoreCase)
               || UnityYamlReader.IsUnityYamlFile(fullPath);
    }

    /// <summary>
    /// Appends one length-prefixed component so adjacent components can never be confused
    /// (a missing or non-candidate file is a zero-length component).
    /// </summary>
    private static void AppendFile(IncrementalHash hash, string? path)
    {
        Span<byte> lengthPrefix = stackalloc byte[8];
        if (path is null || !File.Exists(path))
        {
            BinaryPrimitives.WriteInt64LittleEndian(lengthPrefix, 0);
            hash.AppendData(lengthPrefix);
            return;
        }

        using var stream = File.OpenRead(path);
        BinaryPrimitives.WriteInt64LittleEndian(lengthPrefix, stream.Length);
        hash.AppendData(lengthPrefix);

        Span<byte> buffer = stackalloc byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            hash.AppendData(buffer[..read]);
        }
    }
}
