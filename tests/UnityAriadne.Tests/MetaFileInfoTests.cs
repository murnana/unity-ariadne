using UnityAriadne.Analyzer.Scanning;

namespace UnityAriadne.Tests;

public class MetaFileInfoTests
{
    private static MetaFileInfo ParseFixtureMeta(string relativePath)
    {
        var meta = MetaFileInfo.ParseFile(Path.Combine(Fixtures.SampleProject, relativePath));
        Assert.NotNull(meta);
        return meta;
    }

    [FixtureFact]
    public void FolderMeta_HasFolderFlagAndGuid()
    {
        var meta = ParseFixtureMeta("Assets/Art.meta");

        Assert.Equal("37a0e972f0e35534e8e89141d0b76c8b", meta.Guid);
        Assert.True(meta.IsFolder);
    }

    [FixtureFact]
    public void ScriptMeta_HasMonoImporter()
    {
        var meta = ParseFixtureMeta("Assets/Scripts/RuntimeLoader.cs.meta");

        Assert.Equal("469508434b803884f86a5989866fa0e7", meta.Guid);
        Assert.False(meta.IsFolder);
        Assert.Equal("MonoImporter", meta.ImporterType);
    }

    [FixtureFact]
    public void MetaWithoutImporterSection_ParsesGuidWithNullImporter()
    {
        // Unity 6 omits the importer section when all settings are defaults;
        // only fileFormatVersion, guid and AssetOrigin remain.
        var meta = ParseFixtureMeta("Assets/Scripts/ButtonAnimation.cs.meta");

        Assert.Equal("91bb064945ae772418acdb0f05f5ce2d", meta.Guid);
        Assert.Null(meta.ImporterType);
    }

    [FixtureFact]
    public void ScriptableObjectMeta_ExposesMainObjectFileId()
    {
        var meta = ParseFixtureMeta("Assets/URP/DefaultVolume.asset.meta");

        Assert.Equal("334d6a745cff9df40a60ceff2f3a6559", meta.Guid);
        Assert.Equal("NativeFormatImporter", meta.ImporterType);
        Assert.Equal(11400000, meta.MainObjectFileId);
    }

    [FixtureFact]
    public void ModelImporterMeta_IgnoresNestedGuidsInExternalObjects()
    {
        // This .meta contains externalObjects entries with their own nested
        // "guid:" values; only the top-level guid may be returned. The file
        // name also contains a Cyrillic "А".
        var meta = ParseFixtureMeta("Assets/Art/Meshes/Compositions/Composition_4А.fbx.meta");

        Assert.Equal("d05707a312f67ba44be89f1b110c468c", meta.Guid);
        Assert.Equal("ModelImporter", meta.ImporterType);
    }

    [Fact]
    public void MissingGuid_ReturnsNull()
    {
        using var reader = new StringReader("fileFormatVersion: 2\nDefaultImporter:\n  userData: \n");

        Assert.Null(MetaFileInfo.Parse(reader));
    }

    [Fact]
    public void EmptyInput_ReturnsNull()
    {
        using var reader = new StringReader("");

        Assert.Null(MetaFileInfo.Parse(reader));
    }
}
