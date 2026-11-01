using System.Text.RegularExpressions;
using UnityAriadne.Analyzer.Scanning;

namespace UnityAriadne.Tests;

public class UnityProjectScannerTests
{
    // Scanning 17k+ .meta files takes a moment; share one scan across all tests.
    private static readonly Lazy<GuidDictionary> FixtureScan =
        new(() => UnityProjectScanner.Scan(Fixtures.SampleProject));

    private static GuidDictionary Dictionary => FixtureScan.Value;

    [FixtureFact]
    public void Fixture_AllAssetsMetaFilesAreIndexed()
    {
        // fixtures/SampleProject/Assets contains exactly 1048 committed .meta files,
        // of which Assets/Terrain/Shader/.CustomTerrainLit.shader.meta belongs to a
        // dot-prefixed file that Unity does not import.
        Assert.Equal(1047, Dictionary.Entries.Count(e => e.Source == "Assets"));
    }

    [FixtureTheory]
    [InlineData("23d8661265deed742959416ea58475f7", "Assets/Scenes/Demo.unity", "Scene")]
    [InlineData("469508434b803884f86a5989866fa0e7", "Assets/Scripts/RuntimeLoader.cs", "Script")]
    [InlineData("81bad942fceaf4d6bac13a3375f53dcb", "Assets/Terrain/URPTerrain_MAT.mat", "Material")]
    [InlineData("334d6a745cff9df40a60ceff2f3a6559", "Assets/URP/DefaultVolume.asset", "ScriptableObject")]
    [InlineData("37a0e972f0e35534e8e89141d0b76c8b", "Assets/Art", "Folder")]
    [InlineData("d05707a312f67ba44be89f1b110c468c", "Assets/Art/Meshes/Compositions/Composition_4А.fbx", "Model")]
    public void Fixture_KnownGuidsResolve(string guid, string expectedPath, string expectedType)
    {
        Assert.True(Dictionary.TryResolve(guid, out var entry));
        Assert.Equal(expectedPath, entry.Path);
        Assert.Equal(expectedType, entry.AssetType);
        Assert.Equal("Assets", entry.Source);
    }

    [FixtureFact]
    public void Fixture_UnknownGuid_DoesNotResolve()
    {
        Assert.False(Dictionary.TryResolve("00000000000000000000000000000000", out _));
        Assert.Null(Dictionary.Find("00000000000000000000000000000000"));
    }

    [FixtureFact]
    public void Fixture_AllGuidsAreLowercaseHex32()
    {
        Assert.All(Dictionary.Entries, e => Assert.Matches("^[0-9a-f]{32}$", e.Guid));
    }

    [FixtureFact]
    public void Fixture_PathsAreRelativeWithForwardSlashes()
    {
        Assert.All(Dictionary.Entries, e =>
        {
            Assert.False(Path.IsPathRooted(e.Path), e.Path);
            Assert.DoesNotContain('\\', e.Path);
            Assert.False(e.Path.EndsWith(".meta", StringComparison.Ordinal), e.Path);
        });
    }

    [FixtureFact]
    public void Fixture_UnityIgnoredPathsAreExcluded()
    {
        // Package Samples~ folders duplicate the GUIDs of samples imported under
        // Assets/; Unity never imports '~'-suffixed folders, so neither do we.
        Assert.DoesNotContain(Dictionary.Entries, e => Regex.IsMatch(e.Path, "(^|/)[^/]*~(/|$)"));
        Assert.DoesNotContain(Dictionary.Entries, e => Regex.IsMatch(e.Path, "(^|/)\\.[^/]"));
    }

    [FixtureFact]
    public void Fixture_HasNoDuplicateGuidWarnings()
    {
        // Ground truth: after Samples~ filtering the fixture has no GUID
        // collisions, with or without Library/PackageCache present.
        Assert.DoesNotContain(Dictionary.Warnings, w => w.Message.StartsWith("duplicate guid", StringComparison.Ordinal));
    }

    [FixtureFact]
    public void Fixture_PackageCacheEntriesResolveWhenPresent()
    {
        if (!Fixtures.HasPackageCache)
        {
            return; // CI: Library/ is gitignored, nothing to assert.
        }

        Assert.Contains(Dictionary.Entries, e => e.Source == "PackageCache");
    }

    [FixtureFact]
    public void MissingScanRoots_AreSkippedWithoutError()
    {
        // A project checkout may lack Packages/ and Library/PackageCache entirely.
        var root = Directory.CreateTempSubdirectory("ariadne-test-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            File.Copy(
                Path.Combine(Fixtures.SampleProject, "Assets", "Art.meta"),
                Path.Combine(root, "Assets", "Art.meta"));

            var dictionary = UnityProjectScanner.Scan(root);

            var entry = Assert.Single(dictionary.Entries);
            Assert.Equal("Assets/Art", entry.Path);
            Assert.Empty(dictionary.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Library/PackageCache/com.unity.shadergraph@89df6040bb1d/Samples~/Common/Foo.png.meta", true)]
    [InlineData("Library/PackageCache/com.unity.shadergraph@89df6040bb1d/Documentation~/index.md.meta", true)]
    [InlineData("Assets/.hidden/Foo.png.meta", true)]
    [InlineData("Assets/Backup~/Foo.png.meta", true)]
    [InlineData("Assets/Art/Rock.png.meta", false)]
    [InlineData("Assets/Samples/Shader Graph/17.0.3/Water.shadergraph.meta", false)]
    public void IsIgnoredByUnity_MatchesUnityImportRules(string path, bool expected)
    {
        Assert.Equal(expected, UnityProjectScanner.IsIgnoredByUnity(path));
    }
}
