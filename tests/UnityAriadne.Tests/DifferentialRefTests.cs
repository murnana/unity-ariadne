using System.Text;
using UnityAriadne.Analyzer.Yaml;

namespace UnityAriadne.Tests;

/// <summary>
/// Differential test (Phase 1b): for every Unity YAML file in the
/// fixture, the full parser must find at least every reference the regex oracle finds
/// (parser ⊇ oracle). Guards against the parser silently dropping references.
/// DO NOT DELETE — this is the regression oracle mandated by CLAUDE.md.
/// </summary>
public class DifferentialRefTests
{
    [FixtureFact]
    public void FullParserFindsEverythingTheRegexOracleFinds()
    {
        var yamlFiles = Directory
            .EnumerateFiles(Path.Combine(Fixtures.SampleProject, "Assets"), "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".meta", StringComparison.Ordinal))
            .Where(UnityYamlReader.IsUnityYamlFile)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Fixture ground truth: 261 Unity YAML asset files under Assets/ (a drop means
        // the header sniffing broke, not that the fixture changed).
        Assert.Equal(261, yamlFiles.Count);

        var failures = new StringBuilder();
        var totalParserRefs = 0;

        foreach (var file in yamlFiles)
        {
            var relative = Path.GetRelativePath(Fixtures.SampleProject, file);

            List<UnityRef> parserRefs;
            try
            {
                parserRefs = UnityYamlReader.ReadFile(file)
                    .SelectMany(ReferenceExtractor.Extract)
                    .ToList();
            }
            catch (Exception e)
            {
                failures.AppendLine($"{relative}: parser threw {e.GetType().Name}: {e.Message}");
                continue;
            }

            totalParserRefs += parserRefs.Count;

            var parserCounts = parserRefs
                .GroupBy(r => (r.FileId, r.Guid))
                .ToDictionary(g => g.Key, g => g.Count());

            var oracleRefs = RegexRefOracle.Extract(File.ReadAllText(file));
            foreach (var group in oracleRefs.GroupBy(r => (r.FileId, r.Guid)))
            {
                var found = parserCounts.GetValueOrDefault(group.Key);
                if (found < group.Count())
                {
                    failures.AppendLine(
                        $"{relative}: oracle found {{fileID: {group.Key.FileId}, guid: {group.Key.Guid ?? "-"}}} " +
                        $"x{group.Count()}, parser only x{found}");
                }
            }
        }

        Assert.True(failures.Length == 0, failures.ToString());

        // Sanity: the fixture is reference-dense; a tiny total means extraction broke.
        Assert.True(totalParserRefs > 10_000, $"suspiciously few references extracted: {totalParserRefs}");
    }
}
