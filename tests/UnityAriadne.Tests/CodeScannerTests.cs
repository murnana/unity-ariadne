using UnityAriadne.Analyzer.Code;
using UnityAriadne.Analyzer.Scanning;
using UnityAriadne.Analyzer.Yaml;

namespace UnityAriadne.Tests;

public class CodeScannerTests
{
    private static ScriptSource Script(string guid, string path, string text) => new(guid, path, text);

    private static CodeScanResult Scan(params ScriptSource[] scripts) =>
        CodeScanner.Scan(scripts, scripts.Select(s => new AssetEntry(s.Guid, s.Path, "Script", "Assets")));

    [Fact]
    public void UnqualifiedNameResolvesByEnclosingNamespace()
    {
        var result = Scan(
            Script("g1", "Assets/A.cs", "namespace X { class A { public B b; } }"),
            Script("g2", "Assets/B.cs", "namespace X { class B { } }"),
            Script("g3", "Assets/B2.cs", "namespace Y { class B { } }"));

        var refs = Assert.Single(result.Scripts);
        Assert.Equal("g1", refs.Guid);
        Assert.Equal([new CodeRef("g2", 11500000, "A.b", RefKind.SerializedField)], refs.Refs);
        Assert.Equal(0, result.AmbiguousTypeRefCount);
    }

    [Fact]
    public void UsingDirectiveBringsNamespaceInScope()
    {
        var result = Scan(
            Script("g1", "Assets/A.cs", "using Y; namespace X { class A : C { } }"),
            Script("g2", "Assets/C.cs", "namespace Y { class C { } }"));

        Assert.Equal("g2", Assert.Single(Assert.Single(result.Scripts).Refs).ToGuid);
    }

    [Fact]
    public void TwoInScopeCandidatesAreAmbiguousAndSkipped()
    {
        var result = Scan(
            Script("g1", "Assets/A.cs", "using Y; using Z; class A { public D d; }"),
            Script("g2", "Assets/D1.cs", "namespace Y { class D { } }"),
            Script("g3", "Assets/D2.cs", "namespace Z { class D { } }"));

        Assert.Empty(result.Scripts);
        Assert.Equal(1, result.AmbiguousTypeRefCount);
    }

    [Fact]
    public void QualifiedNameDisambiguates()
    {
        var result = Scan(
            Script("g1", "Assets/A.cs", "class A { public Y.D d; }"),
            Script("g2", "Assets/D1.cs", "namespace Y { class D { } }"),
            Script("g3", "Assets/D2.cs", "namespace Z { class D { } }"));

        Assert.Equal("g2", Assert.Single(Assert.Single(result.Scripts).Refs).ToGuid);
    }

    [Fact]
    public void AliasUsingResolvesLikeQualifiedName()
    {
        var result = Scan(
            Script("g1", "Assets/A.cs", "using D = Y.D; class A { public D d; }"),
            Script("g2", "Assets/D1.cs", "namespace Y { class D { } }"),
            Script("g3", "Assets/D2.cs", "namespace Z { class D { } }"));

        Assert.Equal("g2", Assert.Single(Assert.Single(result.Scripts).Refs).ToGuid);
    }

    [Fact]
    public void SameFileReferencesAreSkippedNotWritten()
    {
        var result = Scan(
            Script("g1", "Assets/A.cs", "interface IA { } class A : IA { }"));

        Assert.Empty(result.Scripts);
        Assert.Equal(1, result.SameFileRefCount);
    }

    /// <summary>Partial types split over files are one type — every declaring file gets the edge.</summary>
    [Fact]
    public void PartialTypeYieldsEdgeToEveryDeclaringFile()
    {
        var result = Scan(
            Script("g1", "Assets/A.cs", "class A { public P p; }"),
            Script("g2", "Assets/P1.cs", "partial class P { }"),
            Script("g3", "Assets/P2.cs", "partial class P { }"));

        Assert.Equal(
            ["g2", "g3"],
            Assert.Single(result.Scripts).Refs.Select(r => r.ToGuid).Order());
        Assert.Equal(0, result.AmbiguousTypeRefCount);
    }

    [Fact]
    public void NestedTypesResolveOnlyWhenQualified()
    {
        var result = Scan(
            Script("g1", "Assets/A.cs", "class A { public Inner plain; public Outer.Inner qualified; }"),
            Script("g2", "Assets/Outer.cs", "class Outer { public class Inner { } }"));

        Assert.Equal(
            [new CodeRef("g2", 11500000, "A.qualified", RefKind.SerializedField)],
            Assert.Single(result.Scripts).Refs);
    }

    [Fact]
    public void ResourcesLoadResolvesAgainstResourcesFolders()
    {
        var scripts = new[]
        {
            Script("g1", "Assets/A.cs",
                """
                class A
                {
                    void M()
                    {
                        Resources.Load("ui/PANEL");
                        Resources.LoadAll("UI");
                        Resources.Load("Missing");
                    }
                }
                """),
        };
        var assets = new AssetEntry[]
        {
            new("g1", "Assets/A.cs", "Script", "Assets"),
            new("p1", "Assets/Game/Resources/UI/Panel.prefab", "Prefab", "Assets"),
            new("f1", "Assets/Game/Resources/UI", "Folder", "Assets"),
        };

        var result = CodeScanner.Scan(scripts, assets);

        Assert.Equal(
            [
                new CodeRef("p1", 0, """A.Resources.Load("ui/PANEL")""", RefKind.ResourcesLoad),
                new CodeRef("f1", 0, """A.Resources.LoadAll("UI")""", RefKind.ResourcesLoad),
            ],
            Assert.Single(result.Scripts).Refs);
        Assert.Equal(1, result.UnresolvedResourceLoadCount);
    }
}

/// <summary>Code-layer facts over the analyzed fixture graph (Phase 2).</summary>
[Collection("fixture-graph")]
public class CodeLayerFixtureTests(FixtureGraphFixture fixture)
{
    /// <summary>
    /// Edges whose resolution cannot depend on PackageCache presence: IInputProvider and
    /// CameraControllerSettings are declared under Assets/ and shadowed nowhere.
    /// </summary>
    [FixtureFact]
    public void KnownAssetsOnlyCodeEdgesExist()
    {
        Assert.Equal(
            ["Assets/Scripts/InputProviders/InputProvider.cs|InputProvider : IInputProvider"],
            QueryCodeRefs("r.ref_kind = 'inherits' AND a2.path = 'Assets/Scripts/Interfaces/IInputProvider.cs'"));

        // RTSCameraController, QualitySettingsController and StatsCanvasController all
        // do GetComponent<IInputProvider>().
        Assert.Equal(3, GraphDatabaseTests.QueryLong(fixture.Db,
            """
            SELECT COUNT(*) FROM refs r
            JOIN assets a2 ON a2.guid = r.to_guid
            WHERE r.ref_kind = 'get_component'
              AND a2.path = 'Assets/Scripts/Interfaces/IInputProvider.cs'
            """));

        Assert.Contains(
            "Assets/Scripts/Camera/RTSCameraController.cs|RTSCameraController.currentSettings",
            QueryCodeRefs("r.ref_kind = 'serialized_field' AND a2.path = 'Assets/Scripts/Camera/CameraControllerSettings.cs'"));

        // Backlog kinds, both endpoints under Assets/: the editor scripts inspect
        // project types, and RTSCameraController caches a settings object in a
        // non-serialized private field.
        Assert.Equal(
            [
                "Assets/Scripts/Camera/Editor/CameraControllerSettingsEditor.cs|CameraControllerSettingsEditor.[CustomEditor(typeof(CameraControllerSettings))]",
                "Assets/Scripts/TutorialInfo/Editor/FKReadmeEditor.cs|FKReadmeEditor.[CustomEditor(typeof(FKReadme))]",
            ],
            QueryCodeRefs("r.ref_kind = 'custom_editor' AND a.source = 'Assets'"));
        Assert.Contains(
            "Assets/Scripts/Camera/RTSCameraController.cs|RTSCameraController._prevZoomLevelData",
            QueryCodeRefs("r.ref_kind = 'nonserialized_field' AND a2.path = 'Assets/Scripts/Camera/CameraControllerSettings.cs'"));
    }

    /// <summary>Code refs hang off a MonoScript object — the object uses_script refs point at.</summary>
    [FixtureFact]
    public void CodeRefsHangOffMonoScriptObjects()
    {
        Assert.Equal(
            ["11500000|115|InputProvider"],
            GraphDatabaseTests.QueryStrings(fixture.Db,
                """
                SELECT o.file_id || '|' || o.class_id || '|' || o.name
                FROM objects o
                JOIN assets a ON a.id = o.asset_id
                WHERE a.path = 'Assets/Scripts/InputProviders/InputProvider.cs'
                """));

        // Every code-kind ref originates from a MonoScript object, and no code ref
        // targets an unresolved GUID (targets always come from the GUID dictionary).
        Assert.Equal(0, GraphDatabaseTests.QueryLong(fixture.Db,
            $"""
             SELECT COUNT(*) FROM refs r
             JOIN objects o ON o.id = r.from_object_id
             WHERE r.ref_kind IN {CodeKindsSql}
               AND (o.file_id <> 11500000 OR o.class_id <> 115)
             """));
        Assert.Equal(0, GraphDatabaseTests.QueryLong(fixture.Db,
            $"""
             SELECT COUNT(*) FROM refs r
             JOIN assets a2 ON a2.guid = r.to_guid
             WHERE r.ref_kind IN {CodeKindsSql} AND a2.path IS NULL
             """));
    }

    /// <summary>
    /// Exact code census under Assets/, PackageCache-dependent (TMP/Cinemachine/uGUI field
    /// types only resolve when their package sources were scanned) — never runs in CI.
    /// </summary>
    [FixtureFact]
    public void WithPackageCacheAssetsCodeCensusMatchesGroundTruth()
    {
        if (!Fixtures.HasPackageCache)
        {
            return;
        }

        Assert.Equal(
            [
                "custom_editor: 2", "get_component: 6", "inherits: 1",
                "nonserialized_field: 32", "serialized_field: 20",
            ],
            GraphDatabaseTests.QueryStrings(fixture.Db,
                $"""
                 SELECT r.ref_kind || ': ' || COUNT(*)
                 FROM refs r
                 JOIN objects o ON o.id = r.from_object_id
                 JOIN assets a ON a.id = o.asset_id
                 WHERE a.source = 'Assets' AND r.ref_kind IN {CodeKindsSql}
                 GROUP BY r.ref_kind
                 ORDER BY r.ref_kind
                 """));

        // 9 of the 17 Assets scripts have resolved refs (the others only touch DLL
        // types) — the two Editor scripts joined via their [CustomEditor] edges.
        Assert.Equal(9, GraphDatabaseTests.QueryLong(fixture.Db,
            """
            SELECT COUNT(*) FROM objects o
            JOIN assets a ON a.id = o.asset_id
            WHERE a.source = 'Assets' AND o.file_id = 11500000
            """));

        // Cross-boundary string ref: TMP package scripts load the project's settings
        // asset by Resources path — invisible to the whole asset layer (Phase 2 plan).
        // 4 = TMP_Settings.cs ×2 + TMP_SettingsEditor.cs + TMP_PostBuildProcessHandler.cs.
        Assert.Equal(4, GraphDatabaseTests.QueryLong(fixture.Db,
            """
            SELECT COUNT(*) FROM refs r
            JOIN assets a2 ON a2.guid = r.to_guid
            WHERE r.ref_kind = 'resources_load'
              AND a2.path = 'Assets/TextMesh Pro/Resources/TMP Settings.asset'
            """));
    }

    internal const string CodeKindsSql =
        "('inherits','serialized_field','nonserialized_field','get_component','custom_editor','resources_load')";

    private IReadOnlyList<string> QueryCodeRefs(string condition) =>
        GraphDatabaseTests.QueryStrings(fixture.Db,
            $"""
             SELECT a.path || '|' || r.property_path
             FROM refs r
             JOIN objects o ON o.id = r.from_object_id
             JOIN assets a ON a.id = o.asset_id
             JOIN assets a2 ON a2.guid = r.to_guid
             WHERE {condition}
             ORDER BY a.path, r.property_path
             """);
}
