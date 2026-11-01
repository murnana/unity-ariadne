using UnityAriadne.Analyzer.Code;
using UnityAriadne.Analyzer.Scanning;
using UnityAriadne.Analyzer.Storage;
using UnityAriadne.Analyzer.Yaml;
using UnityAriadne.Graph;

namespace UnityAriadne.Tests;

/// <summary>
/// Addressables backlog item. All Unity YAML here is real Editor output from
/// fixtures/Addressables (Unity 6000.0.78f1 + Addressables 2.3.16, generated in
/// batchmode on a fixture clone — see fixtures/README.md); tests only copy it.
/// </summary>
public class AddressablesTests
{
    // Baked into the generated fixture: the three assets the batchmode script marked
    // addressable, and the entry guids in the two group assets.
    private const string MaterialGuid = "f3601430fa7f2454cbdab445a8e2b489";
    private const string HousePrefabGuid = "236d74badf5f6244b9ca58b26ed6c760";
    private const string DemoSceneGuid = "23d8661265deed742959416ea58475f7";

    private const string DefaultGroup = "Assets/AddressableAssetsData/AssetGroups/Default Local Group.asset";
    private const string ScenesGroup = "Assets/AddressableAssetsData/AssetGroups/Scenes.asset";
    private const string SmokeScene = "Assets/Scenes/AddressablesSmoke.unity";
    private const string HolderScript = "Assets/Scripts/AddrRefHolder.cs";

    [FixtureFact]
    public void GroupEntriesYieldAddressableEntryRefs()
    {
        var refs = ExtractFromFixtureFile("AddressableAssetsData/AssetGroups/Default Local Group.asset");

        var entries = refs.Where(r => r.FromAddressableEntry).ToList();
        Assert.Equal(
            [
                (HousePrefabGuid, "m_SerializeEntries[0].m_GUID"),
                (MaterialGuid, "m_SerializeEntries[1].m_GUID"),
            ],
            entries.Select(r => (r.Guid, r.PropertyPath)));
        Assert.All(entries, r => Assert.Equal(0, r.FileId));

        // The group's own identity field (a bare m_GUID with no m_Address sibling)
        // must not become a reference.
        Assert.DoesNotContain(refs, r => r.Guid == "7ea3faaa0bef7470d9454c3e6ca6faa2");
    }

    [FixtureFact]
    public void AssetReferenceFieldsYieldRefs_UnsetOnesDoNot()
    {
        var refs = ExtractFromFixtureFile("Scenes/AddressablesSmoke.unity");

        Assert.Equal(
            [
                (MaterialGuid, "anyAsset.m_AssetGUID"),
                (HousePrefabGuid, "housePrefab.m_AssetGUID"),
            ],
            refs.Where(r => r.FromAssetReference).Select(r => (r.Guid, r.PropertyPath)));
    }

    [Fact]
    public void ClassifierMapsExtractionFlagsToKinds()
    {
        var document = new UnityYamlDocument { ClassId = 114, FileId = 11400000 };
        Assert.Equal(
            RefKind.AddressableEntry,
            RefKindClassifier.Classify(
                document,
                new UnityRef { FileId = 0, Guid = MaterialGuid, PropertyPath = "m_SerializeEntries[0].m_GUID", FromAddressableEntry = true },
                isPrefabAsset: false));
        Assert.Equal(
            RefKind.AssetReference,
            RefKindClassifier.Classify(
                document,
                new UnityRef { FileId = 0, Guid = MaterialGuid, PropertyPath = "anyAsset.m_AssetGUID", FromAssetReference = true },
                isPrefabAsset: false));
    }

    [Fact]
    public void AddressablesLoadLiteralsAreCaptured()
    {
        var model = CSharpSourceParser.Parse(
            """
            using UnityEngine.AddressableAssets;

            class C
            {
                void M(string dynamicKey)
                {
                    Addressables.LoadAssetAsync<Material>("Fog/Height");
                    UnityEngine.AddressableAssets.Addressables.LoadSceneAsync("Demo");
                    Addressables.InstantiateAsync("houses");
                    Addressables.LoadAssetAsync<Material>(dynamicKey);
                    SceneManager.LoadSceneAsync("NotAddressables");
                }
            }
            """);

        Assert.Equal(
            [
                new AddressablesLoadSite("Fog/Height", """C.Addressables.LoadAssetAsync("Fog/Height")"""),
                new AddressablesLoadSite("Demo", """C.Addressables.LoadSceneAsync("Demo")"""),
                new AddressablesLoadSite("houses", """C.Addressables.InstantiateAsync("houses")"""),
            ],
            model.AddressablesLoads);
        Assert.Equal(1, model.NonLiteralAddressablesLoads);
    }

    [Fact]
    public void AddressablesKeysResolveViaIndex()
    {
        var scripts = new[]
        {
            new ScriptSource("g1", "Assets/A.cs",
                """
                class A
                {
                    void M()
                    {
                        UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<object>("Houses/PresetB");
                        UnityEngine.AddressableAssets.Addressables.InstantiateAsync("houses");
                        UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<object>("Missing");
                    }
                }
                """),
        };
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["Houses/PresetB"] = ["p1"],
            ["houses"] = ["p1", "p2"],
        };

        var result = CodeScanner.Scan(
            scripts, scripts.Select(s => new AssetEntry(s.Guid, s.Path, "Script", "Assets")), index);

        Assert.Equal(
            [
                new CodeRef("p1", 0, """A.Addressables.LoadAssetAsync("Houses/PresetB")""", RefKind.AddressablesLoad),
                new CodeRef("p1", 0, """A.Addressables.InstantiateAsync("houses")""", RefKind.AddressablesLoad),
                new CodeRef("p2", 0, """A.Addressables.InstantiateAsync("houses")""", RefKind.AddressablesLoad),
            ],
            Assert.Single(result.Scripts).Refs);
        Assert.Equal(1, result.UnresolvedAddressablesLoadCount);
    }

    /// <summary>
    /// End to end over a mini project assembled from the Addressables fixture plus the
    /// three SampleProject assets its entries reference: group entries, AssetReference
    /// fields and literal Addressables keys (address, label, default path address) all
    /// become edges to the right assets.
    /// </summary>
    [FixtureFact]
    public void MiniProjectAnalysisProducesAllThreeAddressablesKinds()
    {
        using var project = CreateAddressablesProject();
        project.AppendLine(HolderScript,
            """
            class AddrLoaderProbe
            {
                void M(object dynamicKey)
                {
                    UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<UnityEngine.Object>("Houses/PresetB");
                    UnityEngine.AddressableAssets.Addressables.LoadSceneAsync("Demo");
                    UnityEngine.AddressableAssets.Addressables.InstantiateAsync("houses");
                    UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<UnityEngine.Object>("Assets/Art/Fog/Materials/HeightFogOriginal_FreeCam.mat");
                    UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<UnityEngine.Object>("no-such-key");
                    UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<UnityEngine.Object>(dynamicKey);
                }
            }
            """);

        using var db = GraphDatabase.CreateInMemory();
        var summary = GraphBuilder.Build(project.Root, UnityProjectScanner.Scan(project.Root), db);

        Assert.Equal(
            [
                $"{DefaultGroup}|m_SerializeEntries[0].m_GUID|Assets/Art/Prefabs/Houses/Preset_House_02_B.prefab",
                $"{DefaultGroup}|m_SerializeEntries[1].m_GUID|Assets/Art/Fog/Materials/HeightFogOriginal_FreeCam.mat",
                $"{ScenesGroup}|m_SerializeEntries[0].m_GUID|Assets/Scenes/Demo.unity",
            ],
            QueryRefs(db, "r.ref_kind = 'addressable_entry'"));

        Assert.Equal(
            [
                $"{SmokeScene}|anyAsset.m_AssetGUID|Assets/Art/Fog/Materials/HeightFogOriginal_FreeCam.mat",
                $"{SmokeScene}|housePrefab.m_AssetGUID|Assets/Art/Prefabs/Houses/Preset_House_02_B.prefab",
            ],
            QueryRefs(db, "r.ref_kind = 'asset_reference'"));

        Assert.Equal(
            [
                $"""{HolderScript}|AddrLoaderProbe.Addressables.InstantiateAsync("houses")|Assets/Art/Prefabs/Houses/Preset_House_02_B.prefab""",
                $"""{HolderScript}|AddrLoaderProbe.Addressables.LoadAssetAsync("Assets/Art/Fog/Materials/HeightFogOriginal_FreeCam.mat")|Assets/Art/Fog/Materials/HeightFogOriginal_FreeCam.mat""",
                $"""{HolderScript}|AddrLoaderProbe.Addressables.LoadAssetAsync("Houses/PresetB")|Assets/Art/Prefabs/Houses/Preset_House_02_B.prefab""",
                $"""{HolderScript}|AddrLoaderProbe.Addressables.LoadSceneAsync("Demo")|Assets/Scenes/Demo.unity""",
            ],
            QueryRefs(db, "r.ref_kind = 'addressables_load'"));
        Assert.Equal(1, summary.UnresolvedAddressablesLoadCount);
        Assert.Equal(1, summary.NonLiteralAddressablesLoadCount);
    }

    /// <summary>
    /// The updater invariant for the new global dependency: editing a group asset's
    /// content (no path change anywhere) must rerun the code layer, because the
    /// address/label tables feed addressables_load resolution.
    /// </summary>
    [FixtureFact]
    public void ChangedGroupAsset_RerunsCodeLayerAndMatchesFullRebuild()
    {
        using var project = CreateAddressablesProject();
        project.AppendLine(HolderScript,
            """
            class AddrLoaderProbe
            {
                void M()
                {
                    UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<UnityEngine.Object>("Houses/PresetB");
                }
            }
            """);

        using var db = GraphDatabase.CreateInMemory();
        GraphBuilder.Build(project.Root, UnityProjectScanner.Scan(project.Root), db);
        Assert.Single(GraphDatabaseTests.QueryStrings(db,
            "SELECT to_guid FROM refs WHERE ref_kind = 'addressables_load'"));

        // Whole-file copy of the other (real Editor) group asset: the "Houses/PresetB"
        // address disappears from the project without any path-set change.
        File.Copy(
            Path.Combine(project.Root, ScenesGroup),
            Path.Combine(project.Root, DefaultGroup),
            overwrite: true);
        var update = GraphUpdater.Update(project.Root, UnityProjectScanner.Scan(project.Root), db);

        Assert.Equal(1, update.ChangedCount);
        Assert.True(update.CodeLayerReanalyzed);
        Assert.Empty(GraphDatabaseTests.QueryStrings(db,
            "SELECT to_guid FROM refs WHERE ref_kind = 'addressables_load'"));
        GraphUpdaterTests.AssertMatchesFullRebuild(project, db);
    }

    private static TempUnityProject CreateAddressablesProject() =>
        new TempUnityProject()
            .WithTreeFrom(Fixtures.Addressables, "Assets")
            .WithAsset("Assets/Art/Fog/Materials/HeightFogOriginal_FreeCam.mat")
            .WithAsset("Assets/Art/Prefabs/Houses/Preset_House_02_B.prefab")
            .WithAsset("Assets/Scenes/Demo.unity");

    private static IReadOnlyList<UnityRef> ExtractFromFixtureFile(string relativePath) =>
        UnityYamlReader.ReadFile(Path.Combine(Fixtures.Addressables, relativePath))
            .SelectMany(ReferenceExtractor.Extract)
            .ToList();

    private static IReadOnlyList<string> QueryRefs(GraphDatabase db, string condition) =>
        GraphDatabaseTests.QueryStrings(db,
            $"""
             SELECT a.path || '|' || r.property_path || '|' || a2.path
             FROM refs r
             JOIN objects o ON o.id = r.from_object_id
             JOIN assets a ON a.id = o.asset_id
             JOIN assets a2 ON a2.guid = r.to_guid
             WHERE {condition}
             ORDER BY a.path, r.property_path
             """);
}
