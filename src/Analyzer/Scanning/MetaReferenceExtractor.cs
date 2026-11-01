using UnityAriadne.Analyzer.Yaml;
using YamlDotNet.RepresentationModel;

namespace UnityAriadne.Analyzer.Scanning;

/// <summary>
/// Extracts asset references from .meta files (Phase 1d): ModelImporter's
/// externalObjects remaps (FBX → Material), ScriptedImporter's <c>script</c> and
/// <c>outputImporterData</c>, etc. All of them get <see cref="RefKind.MetaExternal"/>.
/// </summary>
/// <remarks>
/// Unlike asset files, .meta files are standard single-document YAML (no <c>%TAG</c>
/// directive, no <c>--- !u!</c> headers), so the whole file goes through YamlDotNet.
/// The file's own top-level <c>guid:</c> is a plain scalar, not the reference mapping
/// shape, so the walk never picks it up.
/// One dialect breaks YamlDotNet: PluginImporter's platformData contains an empty
/// block-mapping key (<c>: Any</c>), for which the parser emits a key with no value
/// event. Those files fall back to <see cref="RegexRefOracle"/> (its documented
/// last-resort role), losing property paths but not references.
/// </remarks>
public static class MetaReferenceExtractor
{
    /// <summary>Strict extraction; throws on YAML the parser cannot represent.</summary>
    public static IReadOnlyList<UnityRef> Extract(TextReader reader)
    {
        var stream = new YamlStream();
        stream.Load(reader);
        return stream.Documents.Count > 0
            ? ReferenceExtractor.Extract(stream.Documents[0].RootNode)
            : [];
    }

    public static IReadOnlyList<UnityRef> ExtractFile(string metaPath) =>
        ExtractFile(metaPath, out _);

    /// <summary>
    /// Extraction with the regex fallback. Fallback references carry an empty
    /// <see cref="UnityRef.PropertyPath"/> (the oracle does not know paths).
    /// </summary>
    public static IReadOnlyList<UnityRef> ExtractFile(string metaPath, out bool usedRegexFallback)
    {
        var text = File.ReadAllText(metaPath);
        try
        {
            usedRegexFallback = false;
            return Extract(new StringReader(text));
        }
        catch (Exception e) when (e is YamlDotNet.Core.YamlException or ArgumentException)
        {
            usedRegexFallback = true;
            return RegexRefOracle.Extract(text)
                .Select(r => new UnityRef { FileId = r.FileId, Guid = r.Guid, PropertyPath = "" })
                .ToList();
        }
    }
}
