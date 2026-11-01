using UnityAriadne.Analyzer.Yaml;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace UnityAriadne.Tests;

public class UnityYamlReaderTests
{
    private const string TwoDocumentSample =
        """
        %YAML 1.1
        %TAG !u! tag:unity3d.com,2011:
        --- !u!1 &100
        GameObject:
          m_Name: Root
          m_Component:
          - component: {fileID: 200}
        --- !u!4 &200
        Transform:
          m_GameObject: {fileID: 100}
        """;

    private const string StrippedSample =
        """
        %YAML 1.1
        %TAG !u! tag:unity3d.com,2011:
        --- !u!1 &42 stripped
        GameObject:
          m_CorrespondingSourceObject: {fileID: 919, guid: 52c90d4beb1cea54ba6377a74f9d307d, type: 3}
          m_PrefabInstance: {fileID: 333}
          m_PrefabAsset: {fileID: 0}
        """;

    [Fact]
    public void RawYamlDotNetFailsOnStrippedKeyword()
    {
        // Documents why preprocessing exists: `--- !u!1 &42 stripped` is invalid
        // standard YAML, so feeding a Unity file straight to YamlDotNet throws.
        // If this ever starts passing, the preprocessing layer may be removable.
        var stream = new YamlStream();
        Assert.ThrowsAny<YamlException>(() => stream.Load(new StringReader(StrippedSample)));
    }

    [Fact]
    public void ReadsStrippedDocumentWithFlag()
    {
        var doc = Assert.Single(UnityYamlReader.ReadDocuments(new StringReader(StrippedSample)));

        Assert.True(doc.IsStripped);
        Assert.Equal(1, doc.ClassId);
        Assert.Equal(42, doc.FileId);
        Assert.Equal("GameObject", doc.ClassName);
        Assert.NotNull(doc.Properties);
    }

    [Fact]
    public void EnumeratesAllDocumentsWithClassIdAndFileId()
    {
        var docs = UnityYamlReader.ReadDocuments(new StringReader(TwoDocumentSample)).ToList();

        Assert.Equal(2, docs.Count);
        Assert.Equal((1, 100L, "GameObject", "Root"), (docs[0].ClassId, docs[0].FileId, docs[0].ClassName, docs[0].Name));
        Assert.Equal((4, 200L, "Transform", null), (docs[1].ClassId, docs[1].FileId, docs[1].ClassName, docs[1].Name));
        Assert.All(docs, d => Assert.False(d.IsStripped));
    }

    [Fact]
    public void ParsesNegativeFileId()
    {
        const string sample =
            """
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!114 &-8679921383154817045
            MonoBehaviour:
              m_Name: Negative
            """;

        var doc = Assert.Single(UnityYamlReader.ReadDocuments(new StringReader(sample)));
        Assert.Equal(-8679921383154817045, doc.FileId);
        Assert.Equal(114, doc.ClassId);
    }

    [Fact]
    public void ReadsCrLfInput()
    {
        var crlf = TwoDocumentSample.ReplaceLineEndings("\r\n");
        var docs = UnityYamlReader.ReadDocuments(new StringReader(crlf)).ToList();

        Assert.Equal(2, docs.Count);
        Assert.Equal("Root", docs[0].Name);
    }

    // Fixture ground truth (Animated_Objects.prefab): 21 documents, of which 4 are
    // stripped; verified against `grep '^--- ' <file>` on the checked-in fixture.
    public static string AnimatedObjectsPrefab => Path.Combine(
        Fixtures.SampleProject, "Assets", "Art", "Prefabs", "Compositions", "Animated_Objects.prefab");

    [FixtureFact]
    public void ReadsFixturePrefabWithStrippedDocuments()
    {
        var docs = UnityYamlReader.ReadFile(AnimatedObjectsPrefab).ToList();

        Assert.Equal(21, docs.Count);

        var first = docs[0];
        Assert.Equal(1, first.ClassId);
        Assert.Equal(2119116378955897868, first.FileId);
        Assert.Equal("GameObject", first.ClassName);
        Assert.Equal("SM_Prop_Windmill_03_Blades (1)", first.Name);
        Assert.False(first.IsStripped);

        var stripped = docs.Where(d => d.IsStripped)
            .Select(d => (d.ClassId, d.FileId))
            .ToList();
        Assert.Equal(
            [
                (1, 2488863529568130007),
                (4, 3008941787418265965),
                (4, 8450213591517705662),
                (1, 9083444687178002180),
            ],
            stripped);

        Assert.Equal(
            [3335849973296688774, 8272981677759418965],
            docs.Where(d => d.ClassId == 1001).Select(d => d.FileId).ToList());

        // Stripped documents still have a parsed body.
        Assert.All(docs, d => Assert.NotNull(d.Properties));

        // fileIDs are unique within a file (objects table has UNIQUE(asset_id, file_id)).
        Assert.Equal(docs.Count, docs.Select(d => d.FileId).Distinct().Count());
    }

    [FixtureFact]
    public void IsUnityYamlFileSniffsHeader()
    {
        Assert.True(UnityYamlReader.IsUnityYamlFile(AnimatedObjectsPrefab));

        var shaderGraph = Directory
            .EnumerateFiles(Path.Combine(Fixtures.SampleProject, "Assets"), "*.shadergraph", SearchOption.AllDirectories)
            .First();
        Assert.False(UnityYamlReader.IsUnityYamlFile(shaderGraph));
    }
}
