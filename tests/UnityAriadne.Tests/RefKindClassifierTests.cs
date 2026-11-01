using System.Text;
using UnityAriadne.Analyzer.Yaml;

namespace UnityAriadne.Tests;

public class RefKindClassifierTests
{
    private static UnityYamlDocument ParseSingle(string yaml) =>
        Assert.Single(UnityYamlReader.ReadDocuments(new StringReader(yaml)));

    private static string ClassifySingle(string yaml, string propertyPath, bool isPrefabAsset = false)
    {
        var document = ParseSingle(yaml);
        var reference = Assert.Single(
            ReferenceExtractor.Extract(document), r => r.PropertyPath == propertyPath);
        return RefKindClassifier.Classify(document, reference, isPrefabAsset);
    }

    [Fact]
    public void ScriptReferenceIsUsesScript() =>
        Assert.Equal(RefKind.UsesScript, ClassifySingle(
            """
            --- !u!114 &500
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: 0123456789abcdef0123456789abcdef, type: 3}
            """,
            "m_Script"));

    [Fact]
    public void CorrespondingSourceObjectHasItsOwnKind() =>
        Assert.Equal(RefKind.CorrespondingSource, ClassifySingle(
            """
            --- !u!1 &500 stripped
            GameObject:
              m_CorrespondingSourceObject: {fileID: 919, guid: 0123456789abcdef0123456789abcdef, type: 3}
              m_PrefabInstance: {fileID: 100}
            """,
            "m_CorrespondingSourceObject"));

    /// <summary>Phase 1g: refs surfaced from JSON-in-YAML scalars classify by their flag,
    /// not by property path.</summary>
    [Fact]
    public void EmbeddedJsonRefHasItsOwnKind() =>
        Assert.Equal(RefKind.EmbeddedJson, ClassifySingle(
            """
            --- !u!114 &500
            MonoBehaviour:
              m_SerializableObject: '{"obj":{"fileID":2800000,"guid":"0123456789abcdef0123456789abcdef","type":3}}'
            """,
            "m_SerializableObject.guid"));

    [Fact]
    public void LegacyParentPrefabIsSourcePrefab() =>
        Assert.Equal(RefKind.SourcePrefab, ClassifySingle(
            """
            --- !u!1001 &500
            Prefab:
              m_ParentPrefab: {fileID: 100100000, guid: 0123456789abcdef0123456789abcdef, type: 2}
            """,
            "m_ParentPrefab"));

    // A PrefabInstance whose m_TransformParent is the null ref sits at the file root.
    // In a .prefab that is the variant→base link; anywhere else (scene root, or an
    // instance nested under another transform) it is scene/prefab→nested prefab.
    private const string RootInstanceYaml =
        """
        --- !u!1001 &500
        PrefabInstance:
          m_Modification:
            serializedVersion: 3
            m_TransformParent: {fileID: 0}
            m_Modifications:
            - target: {fileID: 919, guid: 0123456789abcdef0123456789abcdef, type: 3}
              propertyPath: m_Name
              value: Renamed
              objectReference: {fileID: 0}
          m_SourcePrefab: {fileID: 100100000, guid: 0123456789abcdef0123456789abcdef, type: 3}
        """;

    [Fact]
    public void RootInstanceInPrefabIsVariantSourcePrefab() =>
        Assert.Equal(RefKind.SourcePrefab, ClassifySingle(RootInstanceYaml, "m_SourcePrefab", isPrefabAsset: true));

    [Fact]
    public void RootInstanceInSceneIsPrefabInstance() =>
        Assert.Equal(RefKind.PrefabInstance, ClassifySingle(RootInstanceYaml, "m_SourcePrefab", isPrefabAsset: false));

    [Fact]
    public void NestedInstanceInPrefabIsPrefabInstance() =>
        Assert.Equal(RefKind.PrefabInstance, ClassifySingle(
            """
            --- !u!1001 &500
            PrefabInstance:
              m_Modification:
                serializedVersion: 3
                m_TransformParent: {fileID: 8197181670399358650}
              m_SourcePrefab: {fileID: 100100000, guid: 0123456789abcdef0123456789abcdef, type: 3}
            """,
            "m_SourcePrefab",
            isPrefabAsset: true));

    [Fact]
    public void ModificationTargetMatchesBySuffix() =>
        Assert.Equal(RefKind.PrefabModification, ClassifySingle(
            RootInstanceYaml, "m_Modification.m_Modifications[0].target", isPrefabAsset: true));

    [Theory]
    [InlineData("m_Materials[0]")]      // external asset reference
    [InlineData("m_GameObject")]        // same-file object reference
    [InlineData("target")]              // bare `target` outside m_Modifications
    public void EverythingElseIsGeneric(string propertyPath) =>
        Assert.Equal(RefKind.Generic, ClassifySingle(
            """
            --- !u!23 &500
            MeshRenderer:
              m_Materials:
              - {fileID: 2100000, guid: aaaa456789abcdef0123456789abcdef, type: 2}
              m_GameObject: {fileID: 100}
              target: {fileID: 919, guid: aaaa456789abcdef0123456789abcdef, type: 3}
            """,
            propertyPath));

    [FixtureFact]
    public void FixtureVariantPrefabYieldsSourcePrefab()
    {
        // Composition_A_Copy_3 Variant.prefab is a prefab variant: a single root
        // PrefabInstance document and nothing else.
        var path = Path.Combine(
            Fixtures.SampleProject, "Assets", "Art", "Prefabs", "Compositions",
            "Composition_A_Copy_3 Variant.prefab");
        var document = Assert.Single(UnityYamlReader.ReadFile(path));

        var kinds = ReferenceExtractor.Extract(document)
            .Where(r => !r.IsNull)
            .GroupBy(r => RefKindClassifier.Classify(document, r, isPrefabAsset: true))
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(1, kinds[RefKind.SourcePrefab]);
        Assert.False(kinds.ContainsKey(RefKind.PrefabInstance));
        Assert.True(kinds[RefKind.PrefabModification] > 0);
    }

    [FixtureFact]
    public void FixtureNestedInstanceYieldsPrefabInstance()
    {
        // Animated_Objects.prefab has a normal GameObject root; its PrefabInstance
        // &3335849973296688774 is parented under a transform (nested prefab).
        var docs = UnityYamlReader.ReadFile(UnityYamlReaderTests.AnimatedObjectsPrefab)
            .ToDictionary(d => d.FileId);
        var document = docs[3335849973296688774];
        var sourcePrefab = Assert.Single(
            ReferenceExtractor.Extract(document), r => r.PropertyPath == "m_SourcePrefab");

        Assert.Equal(
            RefKind.PrefabInstance,
            RefKindClassifier.Classify(document, sourcePrefab, isPrefabAsset: true));
    }

    /// <summary>
    /// Kind census over every Unity YAML file under Assets/ — fixture ground truth,
    /// like the file count in <see cref="DifferentialRefTests"/>. A source_prefab count
    /// change means the variant detection rule broke (the fixture has 64 variants).
    /// </summary>
    [FixtureFact]
    public void FixtureKindCensusMatchesGroundTruth()
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);

        var yamlFiles = Directory
            .EnumerateFiles(Path.Combine(Fixtures.SampleProject, "Assets"), "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".meta", StringComparison.Ordinal))
            .Where(UnityYamlReader.IsUnityYamlFile);

        foreach (var file in yamlFiles)
        {
            var isPrefabAsset = file.EndsWith(".prefab", StringComparison.Ordinal);
            foreach (var document in UnityYamlReader.ReadFile(file))
            {
                foreach (var reference in ReferenceExtractor.Extract(document))
                {
                    if (reference.IsNull)
                    {
                        continue;
                    }

                    var kind = RefKindClassifier.Classify(document, reference, isPrefabAsset);
                    counts[kind] = counts.GetValueOrDefault(kind) + 1;
                }
            }
        }

        var census = new StringBuilder();
        foreach (var (kind, count) in counts.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            census.AppendLine($"{kind}: {count}");
        }

        // asset_reference 6: not Addressables — URP's ProbeVolumeStreamableAsset stores
        // bare m_AssetGUID strings in "Demo Baking Set.asset", each resolving to a baked
        // .bytes asset next to it. A real dependency the mapping shape could never see.
        Assert.Equal(
            """
            asset_reference: 6
            corresponding_source: 1684
            embedded_json: 26
            generic: 23142
            prefab_instance: 1451
            prefab_modification: 28820
            source_prefab: 64
            uses_script: 3932
            """.ReplaceLineEndings() + Environment.NewLine,
            census.ToString());
    }
}
