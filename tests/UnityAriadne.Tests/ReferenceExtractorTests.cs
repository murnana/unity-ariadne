using UnityAriadne.Analyzer.Yaml;

namespace UnityAriadne.Tests;

public class ReferenceExtractorTests
{
    private static UnityYamlDocument ParseSingle(string yaml) =>
        Assert.Single(UnityYamlReader.ReadDocuments(new StringReader(yaml)));

    [Fact]
    public void ExtractsScriptReferenceWithPropertyPath()
    {
        var doc = ParseSingle(
            """
            --- !u!114 &500
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: 0123456789abcdef0123456789abcdef, type: 3}
            """);

        var reference = Assert.Single(ReferenceExtractor.Extract(doc));
        Assert.Equal("m_Script", reference.PropertyPath);
        Assert.Equal(11500000, reference.FileId);
        Assert.Equal("0123456789abcdef0123456789abcdef", reference.Guid);
        Assert.Equal(3, reference.Type);
        Assert.False(reference.IsNull);
    }

    [Fact]
    public void ExtractsSequenceElementPath()
    {
        var doc = ParseSingle(
            """
            --- !u!23 &500
            MeshRenderer:
              m_Materials:
              - {fileID: 2100000, guid: aaaa456789abcdef0123456789abcdef, type: 2}
              - {fileID: 2100000, guid: bbbb456789abcdef0123456789abcdef, type: 2}
            """);

        var refs = ReferenceExtractor.Extract(doc);
        Assert.Equal(["m_Materials[0]", "m_Materials[1]"], refs.Select(r => r.PropertyPath));
        Assert.Equal("bbbb456789abcdef0123456789abcdef", refs[1].Guid);
    }

    [Fact]
    public void ExtractsNestedModificationTargetPath()
    {
        var doc = ParseSingle(
            """
            --- !u!1001 &500
            PrefabInstance:
              m_Modification:
                m_Modifications:
                - target: {fileID: 919, guid: 52c90d4beb1cea54ba6377a74f9d307d, type: 3}
                  propertyPath: m_Name
                  value: Renamed
                  objectReference: {fileID: 0}
              m_SourcePrefab: {fileID: 100100000, guid: 52c90d4beb1cea54ba6377a74f9d307d, type: 3}
            """);

        var refs = ReferenceExtractor.Extract(doc);
        Assert.Equal(
            [
                "m_Modification.m_Modifications[0].target",
                "m_Modification.m_Modifications[0].objectReference",
                "m_SourcePrefab",
            ],
            refs.Select(r => r.PropertyPath));
    }

    [Fact]
    public void SameFileReferenceHasNullGuid_AndFileIdZeroIsNullRef()
    {
        var doc = ParseSingle(
            """
            --- !u!4 &500
            Transform:
              m_GameObject: {fileID: 100}
              m_Father: {fileID: 0}
            """);

        var refs = ReferenceExtractor.Extract(doc);
        Assert.Equal(2, refs.Count);

        Assert.Equal("m_GameObject", refs[0].PropertyPath);
        Assert.Null(refs[0].Guid);
        Assert.False(refs[0].IsNull);

        Assert.Equal("m_Father", refs[1].PropertyPath);
        Assert.True(refs[1].IsNull);
    }

    [Fact]
    public void DoesNotTreatOrdinaryMappingsAsReferences()
    {
        var doc = ParseSingle(
            """
            --- !u!4 &500
            Transform:
              m_LocalRotation: {x: -0, y: -0.02969217, z: -0, w: 0.99955916}
              m_LocalPosition: {x: -0.48, y: 0, z: -0.3}
              serializedVersion: 2
            """);

        Assert.Empty(ReferenceExtractor.Extract(doc));
    }

    /// <summary>
    /// Phase 1g: VFX assets hide refs in JSON inside YAML string scalars
    /// (m_SerializableObject) — the extractor must surface them alongside mapping refs.
    /// </summary>
    [Fact]
    public void ExtractsEmbeddedJsonFromStringScalar()
    {
        var doc = ParseSingle(
            """
            --- !u!114 &8926484042661614526
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: 70a331b1d86cc8d4aa106ccbe0da5852, type: 3}
              m_SerializableObject: '{"obj":{"fileID":2800000,"guid":"563d9d8f424e0d94096860abee495508","type":3}}'
            """);

        var refs = ReferenceExtractor.Extract(doc);
        Assert.Equal(2, refs.Count);

        var embedded = Assert.Single(refs, r => r.FromEmbeddedJson);
        Assert.Equal("m_SerializableObject.guid", embedded.PropertyPath);
        Assert.Equal("563d9d8f424e0d94096860abee495508", embedded.Guid);
        Assert.Equal(2800000, embedded.FileId);
        Assert.False(refs[0].FromEmbeddedJson); // the mapping-shaped m_Script ref
    }

    [FixtureFact]
    public void ExtractsKnownReferencesFromFixturePrefab()
    {
        var docs = UnityYamlReader.ReadFile(UnityYamlReaderTests.AnimatedObjectsPrefab)
            .ToDictionary(d => d.FileId);

        // MeshFilter &2675145187488640587 → m_Mesh {fileID: 4300002, guid: d67317..., type: 3}
        var meshRef = Assert.Single(
            ReferenceExtractor.Extract(docs[2675145187488640587]),
            r => r.PropertyPath == "m_Mesh");
        Assert.Equal(4300002, meshRef.FileId);
        Assert.Equal("d67317085fc12794fa45adba87d81f84", meshRef.Guid);

        // Animator &7995311960276124563 → m_Controller {fileID: 9100000, guid: eecd5d..., type: 2}
        var controllerRef = Assert.Single(
            ReferenceExtractor.Extract(docs[7995311960276124563]),
            r => r.PropertyPath == "m_Controller");
        Assert.Equal("eecd5d65212ed894580e3bb0d7d089ae", controllerRef.Guid);

        // GameObject &2119116378955897868 lists its components by same-file fileID.
        var componentRefs = ReferenceExtractor.Extract(docs[2119116378955897868])
            .Where(r => r.PropertyPath.StartsWith("m_Component[", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(4, componentRefs.Count);
        Assert.Equal("m_Component[0].component", componentRefs[0].PropertyPath);
        Assert.Equal(185627728412271779, componentRefs[0].FileId);
        Assert.All(componentRefs, r => Assert.Null(r.Guid));

        // PrefabInstance &3335849973296688774 → m_SourcePrefab {guid: 52c90d...}
        var sourcePrefab = Assert.Single(
            ReferenceExtractor.Extract(docs[3335849973296688774]),
            r => r.PropertyPath == "m_SourcePrefab");
        Assert.Equal("52c90d4beb1cea54ba6377a74f9d307d", sourcePrefab.Guid);
        Assert.Equal(100100000, sourcePrefab.FileId);
    }
}
