using System.Text.RegularExpressions;
using UnityAriadne.Analyzer.Yaml;

namespace UnityAriadne.Analyzer.Json;

/// <summary>
/// Extracts asset-guid references from JSON text (Phase 1g): .shadergraph/.shadersubgraph
/// files, and the JSON that VFX assets embed in YAML string scalars
/// (<c>m_SerializableObject: '{"obj":{"fileID":…,"guid":"…","type":…}}'</c>).
/// </summary>
/// <remarks>
/// The JSON is often nested inside JSON strings to varying escape depths
/// (<c>"guid"</c>, <c>\"guid\"</c>, <c>\\\"guid\\\"</c>), so instead of unescaping
/// layer by layer the guid-bearing keys are matched with optional leading backslashes.
/// Only <c>guid</c> and <c>m_FunctionSource</c> carry asset GUIDs; the other 32-hex
/// values in shadergraphs (<c>m_Id</c>, <c>m_ObjectId</c>) are graph-internal node ids
/// and must not match.
/// </remarks>
public static partial class JsonRefScanner
{
    // Unity serializes JSON refs as {"fileID":n,"guid":"…","type":t} with fileID
    // directly before guid; capture it when present, else the ref keeps fileID 0.
    [GeneratedRegex(
        @"(?:\\*""fileID\\*""\s*:\s*(-?\d+)\s*,\s*)?\\*""(guid|m_FunctionSource)\\*""\s*:\s*\\*""([0-9a-fA-F]{32})\\*""")]
    private static partial Regex Reference();

    /// <param name="text">JSON text, or any text that may contain JSON at some escape depth.</param>
    /// <param name="basePath">
    /// Property-path prefix; the matched key is appended, so a .vfx scalar at
    /// <c>…m_SerializableObject</c> yields <c>…m_SerializableObject.guid</c> and a
    /// .shadergraph file (empty prefix) yields <c>guid</c> / <c>m_FunctionSource</c>.
    /// </param>
    public static IReadOnlyList<UnityRef> Scan(string text, string basePath = "")
    {
        // Fast bail: most YAML scalars fed through here contain no quote at all.
        if (!text.Contains('"'))
        {
            return [];
        }

        List<UnityRef>? refs = null;
        foreach (Match match in Reference().Matches(text))
        {
            var key = match.Groups[2].Value;
            (refs ??= []).Add(new UnityRef
            {
                FileId = match.Groups[1].Success ? long.Parse(match.Groups[1].ValueSpan) : 0,
                Guid = match.Groups[3].Value,
                PropertyPath = basePath.Length == 0 ? key : $"{basePath}.{key}",
                FromEmbeddedJson = true,
            });
        }

        return refs ?? (IReadOnlyList<UnityRef>)[];
    }
}
