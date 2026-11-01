using UnityAriadne.Graph;

namespace UnityAriadne.Tests;

public class GraphDatabaseTests
{
    [Fact]
    public void SchemaHasAllTablesAndViews()
    {
        using var db = GraphDatabase.CreateInMemory();

        Assert.Equal(
            ["assets", "objects", "refs", "settings_files"],
            QueryStrings(db, "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name"));
        Assert.Equal(
            ["asset_edges", "unresolved_guids"],
            QueryStrings(db, "SELECT name FROM sqlite_master WHERE type='view' ORDER BY name"));
    }

    [Fact]
    public void WriterRoundTripAggregatesAssetEdges()
    {
        using var db = GraphDatabase.CreateInMemory();
        using (var writer = db.CreateWriter())
        {
            var prefab = writer.AddAsset("aaaa", "Assets/A.prefab", "Prefab", "Assets");
            writer.AddAsset("bbbb", "Assets/B.mat", "Material", "Assets");

            var gameObject = writer.AddObject(prefab, fileId: 100, classId: 1, name: "Root", isStripped: false);
            var component = writer.AddObject(prefab, fileId: 200, classId: 114, name: null, isStripped: true);

            writer.AddRef(gameObject, "bbbb", 2100000, "m_Materials[0]", "generic");
            writer.AddRef(component, "bbbb", 2100000, "m_BaseMaterial", "generic");
            writer.AddRef(gameObject, toGuid: null, toFileId: 200, "m_Component[0].component", "generic");
            writer.Commit();
        }

        // Both refs to bbbb collapse into one asset-level edge with ref_count 2;
        // the same-file ref (to_guid NULL) is excluded from the view.
        Assert.Equal(
            ["aaaa|bbbb|generic|2"],
            QueryStrings(db, "SELECT from_guid || '|' || to_guid || '|' || ref_kind || '|' || ref_count FROM asset_edges"));
        Assert.Equal(1, QueryLong(db, "SELECT COUNT(*) FROM refs WHERE to_guid IS NULL"));
        Assert.Equal(1, QueryLong(db, "SELECT COUNT(*) FROM objects WHERE is_stripped = 1"));
    }

    [Fact]
    public void DisposingWriterWithoutCommitRollsBack()
    {
        using var db = GraphDatabase.CreateInMemory();
        using (var writer = db.CreateWriter())
        {
            writer.AddAsset("aaaa", "Assets/A.prefab", "Prefab", "Assets");
        }

        Assert.Equal(0, QueryLong(db, "SELECT COUNT(*) FROM assets"));
    }

    [Fact]
    public void UnresolvedReportOrdersByRefCountAndListsReferrers()
    {
        using var db = GraphDatabase.CreateInMemory();
        using (var writer = db.CreateWriter())
        {
            var scene = writer.AddAsset("aaaa", "Assets/S.unity", "Scene", "Assets");
            var prefab = writer.AddAsset("bbbb", "Assets/P.prefab", "Prefab", "Assets");
            writer.AddAsset("dead1", null, "Unknown", "Unresolved");
            writer.AddAsset("dead2", null, "Unknown", "Unresolved");

            var sceneObject = writer.AddObject(scene, 1, 1, null, false);
            var prefabObject = writer.AddObject(prefab, 1, 1, null, false);
            writer.AddRef(sceneObject, "dead2", 2100000, "m_Materials[0]", "generic");
            writer.AddRef(sceneObject, "dead2", 2100000, "m_Materials[1]", "generic");
            writer.AddRef(prefabObject, "dead2", 2100000, "m_Materials[0]", "generic");
            writer.AddRef(sceneObject, "dead1", 2100000, "m_Materials[2]", "generic");
            writer.Commit();
        }

        Assert.Collection(
            db.GetUnresolvedGuids(maxSampleReferrers: 1),
            info =>
            {
                Assert.Equal(("dead2", 3L, 2L), (info.Guid, info.RefCount, info.ReferrerCount));
                Assert.Equal(["Assets/P.prefab"], info.SampleReferrers);
            },
            info =>
            {
                Assert.Equal(("dead1", 1L, 1L), (info.Guid, info.RefCount, info.ReferrerCount));
                Assert.Equal(["Assets/S.unity"], info.SampleReferrers);
            });
    }

    [Fact]
    public void CreateFileReplacesExistingDatabaseAndOpenFileReadsIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ariadne-test-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = GraphDatabase.CreateFile(path))
            using (var writer = db.CreateWriter())
            {
                writer.AddAsset("aaaa", "Assets/A.prefab", "Prefab", "Assets");
                writer.Commit();
            }

            using (var reopened = GraphDatabase.OpenFile(path))
            {
                Assert.Equal(1, QueryLong(reopened, "SELECT COUNT(*) FROM assets"));
            }

            // CreateFile is a full rebuild: the previous content is gone.
            using (var recreated = GraphDatabase.CreateFile(path))
            {
                Assert.Equal(0, QueryLong(recreated, "SELECT COUNT(*) FROM assets"));
            }

            Assert.Throws<FileNotFoundException>(() => GraphDatabase.OpenFile(path + ".missing"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    internal static long QueryLong(GraphDatabase db, string sql)
    {
        using var command = db.Connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    internal static IReadOnlyList<string> QueryStrings(GraphDatabase db, string sql)
    {
        using var command = db.Connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var results = new List<string>();
        while (reader.Read())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }
}
