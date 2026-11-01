using System.Text.RegularExpressions;

namespace UnityAriadne.Analyzer.Yaml;

/// <summary>
/// The v1-plan regex reference extractor, kept as the differential-test oracle
/// (Phase 1b): everything it finds must also be found by the full parser.
/// It also serves as the last-resort fallback for files the YAML parser rejects.
/// DO NOT DELETE — CLAUDE.md mandates keeping it for regression detection.
/// </summary>
public static partial class RegexRefOracle
{
    [GeneratedRegex(@"\{\s*fileID:\s*(-?\d+)\s*(?:,\s*guid:\s*([0-9a-fA-F]{32})\s*)?(?:,\s*type:\s*(-?\d+)\s*)?\}")]
    private static partial Regex Reference();

    public static IReadOnlyList<(long FileId, string? Guid)> Extract(string yamlText)
    {
        var refs = new List<(long, string?)>();
        foreach (Match match in Reference().Matches(yamlText))
        {
            refs.Add((
                long.Parse(match.Groups[1].ValueSpan),
                match.Groups[2].Success ? match.Groups[2].Value : null));
        }

        return refs;
    }
}
