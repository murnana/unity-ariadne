using GuidGraph.Graph;

namespace GuidGraph.Tests;

public sealed class UnresolvedHintTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"guidgraph-hint-{Guid.NewGuid():N}.db");

    private const string InPackage = "dead0000000000000000000000000001";
    private const string FromAssets = "dead0000000000000000000000000002";
    private const string Nowhere = "dead0000000000000000000000000003";
    private const string PackageAsset = "aaaa0000000000000000000000000001";

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    private GraphDatabase CreateDatabase()
    {
        var db = GraphDatabase.CreateFile(_dbPath);
        using var writer = db.CreateWriter();
        var cached = writer.AddAsset("aaaa0000000000000000000000000002",
            "Library/PackageCache/com.unity.ugui@1a2b3c/Runtime/Button.prefab", "Prefab", "PackageCache");
        var embedded = writer.AddAsset("aaaa0000000000000000000000000003",
            "Packages/com.example.local/Thing.prefab", "Prefab", "Packages");
        writer.AddAsset(PackageAsset, "Library/PackageCache/com.unity.visualeffectgraph@9f8e/Shaders/S.shader", "Shader", "PackageCache");
        var project = writer.AddAsset("aaaa0000000000000000000000000004", "Assets/Fx.vfx", "VisualEffect", "Assets");
        var lonely = writer.AddAsset("aaaa0000000000000000000000000005", "Assets/Lonely.prefab", "Prefab", "Assets");
        foreach (var guid in new[] { InPackage, FromAssets, Nowhere })
        {
            writer.AddAsset(guid, null, "Unknown", "Unresolved");
        }

        writer.AddRef(writer.AddObject(cached, 1, 114, null, false), InPackage, 11500000, "m_Script", "uses_script");
        writer.AddRef(writer.AddObject(embedded, 1, 114, null, false), InPackage, 11500000, "m_Script", "uses_script");
        writer.AddRef(writer.AddObject(embedded, 2, 114, null, false), InPackage, 11500000, "m_Script", "uses_script");

        var fx = writer.AddObject(project, 1, 114, null, false);
        writer.AddRef(fx, FromAssets, 11500000, "m_Script", "uses_script");
        writer.AddRef(fx, PackageAsset, 4800000, "m_Shader", "generic");

        writer.AddRef(writer.AddObject(lonely, 1, 114, null, false), Nowhere, 11500000, "m_Script", "uses_script");
        writer.Commit();
        return db;
    }

    [Fact]
    public void ReferrersInPackages_NameTheirPackagesWithoutHashes()
    {
        using var db = CreateDatabase();

        var hint = GraphQueries.HintForUnresolved(db, InPackage)!;

        Assert.Equal("referrer_package", hint.Basis);
        Assert.Equal(["com.example.local", "com.unity.ugui"], hint.PackageCandidates);
    }

    [Fact]
    public void AssetReferrer_FallsBackToPackagesItAlsoUses()
    {
        using var db = CreateDatabase();

        var hint = GraphQueries.HintForUnresolved(db, FromAssets)!;

        Assert.Equal("referrer_dependencies", hint.Basis);
        Assert.Equal(["com.unity.visualeffectgraph"], hint.PackageCandidates);
    }

    [Fact]
    public void NoClue_YieldsNoHint()
    {
        using var db = CreateDatabase();

        Assert.Null(GraphQueries.HintForUnresolved(db, Nowhere));
    }

    [Theory]
    [InlineData("Library/PackageCache/com.unity.ugui@44a5/Runtime/A.cs", "com.unity.ugui")]
    [InlineData("Packages/com.example.local/A.cs", "com.example.local")]
    [InlineData("Assets/A.cs", null)]
    public void PackageNameOf_ReadsBothLayouts(string path, string? expected) =>
        Assert.Equal(expected, GraphQueries.PackageNameOf(path));
}
