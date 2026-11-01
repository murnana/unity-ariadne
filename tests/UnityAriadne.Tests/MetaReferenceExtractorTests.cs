using System.Text;
using UnityAriadne.Analyzer.Scanning;
using UnityAriadne.Analyzer.Yaml;

namespace UnityAriadne.Tests;

public class MetaReferenceExtractorTests
{
    [Fact]
    public void ExternalObjectsMaterialRemapIsExtractedWithPath()
    {
        var refs = MetaReferenceExtractor.Extract(new StringReader(
            """
            fileFormatVersion: 2
            guid: a7cbc1af901f8bb428f0d9823bde9752
            ModelImporter:
              serializedVersion: 22200
              externalObjects:
              - first:
                  type: UnityEngine:Material
                  assembly: UnityEngine.CoreModule
                  name: Atlas_Foliage
                second: {fileID: 2100000, guid: 435d79076448f49498f7e82782198a3a, type: 2}
            """));

        var reference = Assert.Single(refs);
        Assert.Equal("ModelImporter.externalObjects[0].second", reference.PropertyPath);
        Assert.Equal(2100000, reference.FileId);
        Assert.Equal("435d79076448f49498f7e82782198a3a", reference.Guid);
    }

    // The file's own guid is a plain scalar, never the {fileID, guid} mapping shape.
    [Fact]
    public void OwnTopLevelGuidIsNotAReference()
    {
        var refs = MetaReferenceExtractor.Extract(new StringReader(
            """
            fileFormatVersion: 2
            guid: a7cbc1af901f8bb428f0d9823bde9752
            DefaultImporter:
              userData:
            """));

        Assert.Empty(refs);
    }

    // Unity wraps long flow mappings across lines (.st9.meta's outputImporterData);
    // that must not break extraction.
    [Fact]
    public void FlowMappingWrappedAcrossLinesIsExtracted()
    {
        var refs = MetaReferenceExtractor.Extract(new StringReader(
            """
            fileFormatVersion: 2
            guid: bbcbcc3c7fa7d3f429e2dba48502fa18
            ScriptedImporter:
              script: {fileID: 12405, guid: 0000000000000000e000000000000000, type: 0}
              outputImporterData: {fileID: 7965120797947565359, guid: bbcbcc3c7fa7d3f429e2dba48502fa18,
                type: 3}
            """));

        Assert.Equal(2, refs.Count);
        Assert.Contains(refs, r => r.PropertyPath == "ScriptedImporter.script");
        Assert.Contains(refs, r => r is
        {
            PropertyPath: "ScriptedImporter.outputImporterData",
            FileId: 7965120797947565359,
            Guid: "bbcbcc3c7fa7d3f429e2dba48502fa18",
        });
    }

    [FixtureFact]
    public void FixtureSt9MetaYieldsAllFourReferences()
    {
        var refs = MetaReferenceExtractor.ExtractFile(Path.Combine(
            Fixtures.SampleProject, "Assets", "Art", "Textures", "Foliage",
            "Trees and Bushes", "TreeLargeStylized_01.st9.meta"));

        Assert.Equal(4, refs.Count);
        Assert.Equal(2, refs.Count(r => r.PropertyPath.StartsWith("ScriptedImporter.externalObjects[", StringComparison.Ordinal)));
        Assert.Contains(refs, r => r.PropertyPath == "ScriptedImporter.script");
        Assert.Contains(refs, r => r.PropertyPath == "ScriptedImporter.outputImporterData");
    }

    // PluginImporter's platformData contains an empty block-mapping key (`: Any`);
    // YamlDotNet emits a key with no value event and the node tree cannot be built.
    private const string EmptyKeyMeta =
        """
        fileFormatVersion: 2
        guid: 9ac5be0090514aa8b35011788efe5757
        PluginImporter:
          platformData:
          - first:
              : Any
            second:
              enabled: 0
          script: {fileID: 11500000, guid: 0123456789abcdef0123456789abcdef, type: 3}
        """;

    [Fact]
    public void EmptyBlockMappingKeyThrowsInStrictMode() =>
        Assert.ThrowsAny<Exception>(() => MetaReferenceExtractor.Extract(new StringReader(EmptyKeyMeta)));

    [Fact]
    public void UnparsableMetaFallsBackToRegexOracle()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".meta");
        File.WriteAllText(path, EmptyKeyMeta);
        try
        {
            var refs = MetaReferenceExtractor.ExtractFile(path, out var usedRegexFallback);

            Assert.True(usedRegexFallback);
            var reference = Assert.Single(refs);
            Assert.Equal(11500000, reference.FileId);
            Assert.Equal("0123456789abcdef0123456789abcdef", reference.Guid);
            Assert.Equal("", reference.PropertyPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Phase 1d differential test, same contract as <see cref="DifferentialRefTests"/>:
    /// over every .meta under Assets/, the full extractor must find at least everything
    /// the regex oracle finds. DO NOT DELETE.
    /// </summary>
    [FixtureFact]
    public void FullExtractorFindsEverythingTheRegexOracleFindsInMetaFiles()
    {
        var metaFiles = Directory
            .EnumerateFiles(Path.Combine(Fixtures.SampleProject, "Assets"), "*.meta", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Fixture ground truth (1a): 1,048 .meta files under Assets/.
        Assert.Equal(1048, metaFiles.Count);

        var failures = new StringBuilder();
        var totalRefs = 0;
        var filesWithRefs = 0;

        foreach (var file in metaFiles)
        {
            var relative = Path.GetRelativePath(Fixtures.SampleProject, file);

            var refs = MetaReferenceExtractor.ExtractFile(file, out var usedRegexFallback);
            if (usedRegexFallback)
            {
                // Only PluginImporter metas (none under the fixture's Assets/) need the
                // fallback; a new one here means the strict parse regressed.
                failures.AppendLine($"{relative}: unexpectedly used the regex fallback");
                continue;
            }

            var nonNull = refs.Count(r => !r.IsNull);
            totalRefs += nonNull;
            if (nonNull > 0)
            {
                filesWithRefs++;
            }

            var parserCounts = refs
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
                        $"x{group.Count()}, extractor only x{found}");
                }
            }
        }

        Assert.True(failures.Length == 0, failures.ToString());

        // Fixture ground truth: 677 ModelImporter externalObjects remaps + 39
        // ScriptedImporter scripts + 25 ScriptedImporter externalObjects + 12
        // outputImporterData.
        Assert.Equal(753, totalRefs);
        Assert.Equal(400, filesWithRefs);
    }
}
