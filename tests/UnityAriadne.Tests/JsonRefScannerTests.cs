using UnityAriadne.Analyzer.Json;
using UnityAriadne.Analyzer.Yaml;

namespace UnityAriadne.Tests;

public class JsonRefScannerTests
{
    [Fact]
    public void ExtractsVfxSerializableObjectShape()
    {
        var reference = Assert.Single(JsonRefScanner.Scan(
            """{"obj":{"fileID":2800000,"guid":"563d9d8f424e0d94096860abee495508","type":3}}""",
            "m_Property.defaultValue.m_SerializableObject"));

        Assert.Equal("563d9d8f424e0d94096860abee495508", reference.Guid);
        Assert.Equal(2800000, reference.FileId);
        Assert.Equal("m_Property.defaultValue.m_SerializableObject.guid", reference.PropertyPath);
        Assert.True(reference.FromEmbeddedJson);
        Assert.False(reference.IsNull);
    }

    // Shadergraph JSON nests refs inside JSON strings; each layer adds a backslash
    // escape (`\"guid\"`, `\\\"guid\\\"`). The scanner must see through all depths.
    [Theory]
    [InlineData("""{"guid": "89c32740b629abb41bf9b65e3a64c373"}""")]
    [InlineData("""{"m_Value": "{\"mesh\":{\"fileID\":10210,\"guid\":\"89c32740b629abb41bf9b65e3a64c373\",\"type\":0}}"}""")]
    [InlineData("""{"s": "{\"inner\":\"{\\\"guid\\\": \\\"89c32740b629abb41bf9b65e3a64c373\\\"}\"}"}""")]
    public void ExtractsGuidKeyAtAnyEscapeDepth(string json)
    {
        var reference = Assert.Single(JsonRefScanner.Scan(json));
        Assert.Equal("89c32740b629abb41bf9b65e3a64c373", reference.Guid);
        Assert.Equal("guid", reference.PropertyPath);
    }

    [Fact]
    public void ExtractsBareFunctionSourceGuidWithoutFileId()
    {
        var reference = Assert.Single(JsonRefScanner.Scan(
            """{"m_FunctionSource": "96de908384869cd409c75efa351d5edf", "m_FunctionName": "F"}"""));

        Assert.Equal("96de908384869cd409c75efa351d5edf", reference.Guid);
        Assert.Equal(0, reference.FileId);
        Assert.Equal("m_FunctionSource", reference.PropertyPath);
    }

    [Fact]
    public void CapturesNegativeFileIdPrecedingGuid()
    {
        var reference = Assert.Single(JsonRefScanner.Scan(
            """{"s": "{\"fileID\":-2362172177983852347,\"guid\":\"dda5bcb0d1e9515498f6e4e038bbefe6\",\"type\":2}"}"""));

        Assert.Equal(-2362172177983852347, reference.FileId);
        Assert.Equal("dda5bcb0d1e9515498f6e4e038bbefe6", reference.Guid);
    }

    /// <summary>
    /// Shadergraphs are full of other 32-hex values — node ids (m_Id/m_ObjectId) and
    /// unquoted YAML-style guids — none of which are JSON asset references.
    /// </summary>
    [Theory]
    [InlineData("""{"m_ObjectId": "153b138ddd88e7b42a0c29bde1e7142c"}""")]
    [InlineData("""{"m_Id": "153b138ddd88e7b42a0c29bde1e7142c"}""")]
    [InlineData("""{"guid": "not-a-guid"}""")]
    [InlineData("m_Script: {fileID: 11500000, guid: 153b138ddd88e7b42a0c29bde1e7142c, type: 3}")]
    [InlineData("plain scalar without any json")]
    public void IgnoresNonReferenceContent(string text) =>
        Assert.Empty(JsonRefScanner.Scan(text));

    [FixtureFact]
    public void ScansWholeFixtureShaderGraphFile()
    {
        var refs = JsonRefScanner.Scan(File.ReadAllText(
            Path.Combine(Fixtures.SampleProject, "Assets", "Shaders", "CustomTree.shadergraph")));

        // 4 texture/mesh refs in nested JSON strings, no m_FunctionSource, and none of
        // the thousands of m_Id/m_ObjectId node ids.
        Assert.Equal(4, refs.Count);
        Assert.All(refs, r => Assert.Equal("guid", r.PropertyPath));
        Assert.Contains(refs, r => r.Guid == "89c32740b629abb41bf9b65e3a64c373");
    }
}
