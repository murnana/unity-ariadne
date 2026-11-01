using UnityAriadne.Analyzer.Settings;
using UnityAriadne.Analyzer.Yaml;

namespace UnityAriadne.Tests;

public class ProjectSettingsScannerTests
{
    private static IReadOnlyList<ProjectSettingsRef> ScanFixture() =>
        ProjectSettingsScanner.Scan(Fixtures.SampleProject);

    [FixtureFact]
    public void BuildScenesAreExtractedFromEditorBuildSettings()
    {
        var scenes = ScanFixture()
            .Where(r => r.Ref.PropertyPath.StartsWith("m_Scenes[", StringComparison.Ordinal))
            .ToList();

        Assert.All(scenes, r =>
        {
            Assert.Equal("ProjectSettings/EditorBuildSettings.asset", r.SettingsPath);
            Assert.Equal(RefKind.BuildSettings, r.Kind);
        });
        Assert.Collection(
            scenes,
            r => Assert.Equal(("m_Scenes[0]", "a2bcfd0088dab57498427eb47330d215"), (r.Ref.PropertyPath, r.Ref.Guid)),   // Loader.unity
            r => Assert.Equal(("m_Scenes[1]", "23d8661265deed742959416ea58475f7"), (r.Ref.PropertyPath, r.Ref.Guid)));  // Demo.unity
    }

    // m_configObjects entries are ordinary {fileID, guid} refs; being in
    // EditorBuildSettings.asset makes them build_settings too.
    [FixtureFact]
    public void ConfigObjectsInEditorBuildSettingsAreBuildSettings()
    {
        var reference = Assert.Single(
            ScanFixture(),
            r => r.SettingsPath == "ProjectSettings/EditorBuildSettings.asset"
                 && !r.Ref.PropertyPath.StartsWith("m_Scenes[", StringComparison.Ordinal));

        Assert.Equal(RefKind.BuildSettings, reference.Kind);
        Assert.Equal("57aa1a4c82454616bed26f7b365eb456", reference.Ref.Guid);
    }

    [FixtureFact]
    public void GraphicsSettingsRenderPipelinesAreFound()
    {
        var graphics = ScanFixture()
            .Where(r => r.SettingsPath == "ProjectSettings/GraphicsSettings.asset")
            .ToList();

        Assert.All(graphics, r => Assert.Equal(RefKind.Generic, r.Kind));

        // m_CustomRenderPipeline (the URP asset) and the always-included built-in
        // shaders (guid 0000000000000000f000000000000000).
        Assert.Contains(graphics, r => r is
        {
            Ref: { PropertyPath: "m_CustomRenderPipeline", Guid: "24e04cb166586494ba56cb3fc3ea3f41" },
        });
        Assert.Contains(graphics, r =>
            r.Ref.PropertyPath.StartsWith("m_AlwaysIncludedShaders[", StringComparison.Ordinal)
            && r.Ref.Guid == "0000000000000000f000000000000000");
    }

    /// <summary>
    /// Census over the fixture's ProjectSettings — ground truth like the kind census in
    /// <see cref="RefKindClassifierTests"/>. A change means extraction or the fixture moved.
    /// </summary>
    [FixtureFact]
    public void FixtureSettingsCensusMatchesGroundTruth()
    {
        var refs = ScanFixture();

        var byKind = refs
            .GroupBy(r => r.Kind)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}: {g.Count()}");

        Assert.Equal(EXPECTED_KINDS, string.Join(Environment.NewLine, byKind));

        var byFile = refs
            .GroupBy(r => r.SettingsPath)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}: {g.Count()}");

        Assert.Equal(EXPECTED_FILES, string.Join(Environment.NewLine, byFile));
    }

    // Fixture ground truth. uses_script comes from ScriptableObject-backed settings
    // (Timeline, ShaderGraph, URP, PackageManager) whose documents carry m_Script.
    private const string EXPECTED_KINDS =
        """
        build_settings: 3
        generic: 31
        uses_script: 4
        """;

    private const string EXPECTED_FILES =
        """
        ProjectSettings/EditorBuildSettings.asset: 3
        ProjectSettings/EditorSettings.asset: 1
        ProjectSettings/GraphicsSettings.asset: 17
        ProjectSettings/PackageManagerSettings.asset: 1
        ProjectSettings/ProjectSettings.asset: 2
        ProjectSettings/QualitySettings.asset: 5
        ProjectSettings/ShaderGraphSettings.asset: 1
        ProjectSettings/TimelineSettings.asset: 1
        ProjectSettings/URPProjectSettings.asset: 1
        ProjectSettings/VFXManager.asset: 6
        """;
}
