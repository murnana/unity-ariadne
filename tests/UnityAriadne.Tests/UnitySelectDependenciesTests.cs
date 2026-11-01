using UnityAriadne.Graph;

namespace UnityAriadne.Tests;

/// <summary>
/// Phase 1 acceptance criterion 2: the tool's graph against Unity's own answers.
/// fixtures/unity-select-dependencies.txt is AssetDatabase.GetDependencies output
/// (direct and recursive, self excluded, sorted) for six representative assets,
/// dumped on 2026-07-06 by Unity 6000.0.78f1 batchmode from a throwaway clone of the
/// fixture.
///
/// The comparison is exact up to two documented one-way differences:
/// <list type="bullet">
/// <item>The tool additionally reports BuiltIn pseudo-assets and unresolved GUIDs,
/// which Unity cannot show — asserted to be the only extras.</item>
/// <item>Unity additionally sees through contents the tool does not parse: binary
/// import results (LightingData.asset → its scene, FBX → default URP material/shader)
/// and importer-assigned script associations. Asserted to be the only misses via
/// <see cref="IsKnownGap"/>. Phase 1g closed the JSON gaps (.shadergraph files, .vfx
/// JSON-in-YAML scalars), so those are no longer allowed here.</item>
/// </list>
/// </summary>
[Collection("fixture-graph")]
public class UnitySelectDependenciesTests(FixtureGraphFixture fixture)
{
    private const string PackageCachePrefix = "Library/PackageCache/";

    [FixtureFact]
    public void ToolGraphMatchesUnityGetDependencies()
    {
        // Unity's dump lists package deps by their virtual Packages/ path, which only
        // maps to the physical Library/PackageCache/ scan on machines that opened the
        // fixture in Unity.
        if (!Fixtures.HasPackageCache)
        {
            return;
        }

        var unityDeps = File.ReadLines(Fixtures.UnitySelectDependencies)
            .Select(line => line.Split('|'))
            .GroupBy(parts => (Target: parts[0], Recursive: parts[1] == "rec"))
            .ToDictionary(g => g.Key, g => g.Select(parts => parts[2]).ToHashSet(StringComparer.Ordinal));
        Assert.Equal(12, unityDeps.Count); // 6 assets × direct/recursive

        var (pathsByGuid, sourcesByGuid, edges) = LoadGraph();
        var guidsByPath = pathsByGuid
            .Where(p => p.Value is not null)
            .ToDictionary(p => p.Value!, p => p.Key, StringComparer.Ordinal);

        var unexplained = new List<string>();
        foreach (var ((target, recursive), expected) in unityDeps)
        {
            var targetGuid = guidsByPath[target];
            var toolGuids = recursive ? Closure(edges, targetGuid) : DirectDeps(edges, targetGuid);
            toolGuids.Remove(targetGuid);
            var toolPaths = toolGuids
                .Select(g => pathsByGuid[g])
                .Where(p => p is not null)
                .Select(p => NormalizePackagePath(p!))
                .ToHashSet(StringComparer.Ordinal);

            unexplained.AddRange(expected.Except(toolPaths)
                .Where(missed => !IsKnownGap(target, recursive, missed, guidsByPath, edges, pathsByGuid))
                .Select(missed => $"{target} ({(recursive ? "rec" : "dir")}): {missed}"));

            // Everything the tool reports beyond Unity must be a pseudo/unresolved
            // asset Unity cannot list — never a spurious edge to a real asset.
            foreach (var extraGuid in toolGuids.Where(g => !expected.Contains(NormalizePackagePath(pathsByGuid[g] ?? ""))))
            {
                Assert.Contains(sourcesByGuid[extraGuid], (string[])["BuiltIn", "Unresolved"]);
            }
        }

        Assert.True(unexplained.Count == 0,
            $"Unity sees {unexplained.Count} dependencies the tool does not:\n{string.Join("\n", unexplained)}");
    }

    /// <summary>
    /// Associations that exist only in Unity's import results (Library artifacts),
    /// never in any source file: the runtime/editor scripts Unity attaches to imported
    /// sub-assets (.inputactions → InputActionAsset, shadergraph → its metadata
    /// classes) and shader-to-shader dependencies declared by name string inside
    /// .shader source (TerrainLit → TerrainLitBase).
    /// </summary>
    private static readonly HashSet<string> ImportResultOnlyGaps = new(StringComparer.Ordinal)
    {
        "Packages/com.unity.inputsystem/InputSystem/Actions/InputActionAsset.cs",
        "Packages/com.unity.inputsystem/InputSystem/Actions/InputActionReference.cs",
        "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/UniversalMetadata.cs",
        "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitBase.shader",
        "Packages/com.unity.shadergraph/Editor/Data/SubGraph/SubGraphAsset.cs",
        "Packages/com.unity.shadergraph/Editor/Generation/Targets/Fullscreen/FullscreenMetaData.cs",
        "Packages/com.unity.shadergraph/Editor/Importers/ShaderGraphMetadata.cs",
        "Packages/com.unity.shadergraph/Editor/Importers/ShaderSubGraphMetadata.cs",
    };

    /// <summary>The documented parse-boundary misses — anything else fails the test.</summary>
    private static bool IsKnownGap(
        string target, bool recursive, string missedPath,
        Dictionary<string, string> guidsByPath,
        Dictionary<string, List<string>> edges,
        Dictionary<string, string?> pathsByGuid)
    {
        switch (target, recursive)
        {
            // The FBX's imported (embedded) materials reference URP's Lit shader; that
            // reference exists only in the binary import result. Recursive mode finds
            // the shader anyway — via the remapped materials the .meta does declare.
            case ("Assets/Art/Meshes/Compositions/Composition_148.fbx", false):
                return missedPath == "Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader";

            // A model without a material remap falls back to URP's default Lit
            // material inside the binary import result.
            case ("Assets/Art/Prefabs/Compositions/Animated_Objects.prefab", true):
                return missedPath == "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat";

            // CustomTree.shadergraph's guid refs are parsed since 1g; what remains
            // invisible is only the importer-assigned script associations.
            case ("Assets/Art/Prefabs/Foliage/Trees and Bushes/SM_Env_Bush_01.prefab", true):
                return ImportResultOnlyGaps.Contains(missedPath);

            // Loader references Demo/LightingData.asset (captured), but LightingData is
            // always binary and its m_Scene reference to Demo.unity is invisible — so
            // everything reachable only through Demo.unity is missed, as is everything
            // behind the other opaque sources inside Demo's subtree:
            //  - baked-lighting artifacts under Assets/Scenes/Demo/ (probe .exr,
            //    probe-volume .bytes), referenced only from binary lighting data;
            //  - .terrainlayer assets, referenced only from binary TerrainData.asset.
            // Anything the parser can reach onward from those entry points is allowed
            // too, hence the closure seeded with all of them. (Until 1g the JSON-embedded
            // guids were a third opaque source; they are parsed now and no longer seeded.)
            case ("Assets/Scenes/Loader.unity", true):
                if (missedPath.StartsWith("Assets/Scenes/Demo/", StringComparison.Ordinal)
                    || ImportResultOnlyGaps.Contains(missedPath))
                {
                    return true;
                }

                var seeds = new List<string> { guidsByPath["Assets/Scenes/Demo.unity"] };
                seeds.AddRange(pathsByGuid
                    .Where(p => p.Value?.EndsWith(".terrainlayer", StringComparison.Ordinal) == true)
                    .Select(p => p.Key));
                var allowed = Closure(edges, seeds);
                return missedPath == "Assets/Scenes/Demo.unity"
                       || allowed.Select(g => pathsByGuid[g])
                           .Any(p => p is not null && NormalizePackagePath(p) == missedPath);

            default:
                return false;
        }
    }

    private static HashSet<string> DirectDeps(Dictionary<string, List<string>> edges, string fromGuid) =>
        edges.TryGetValue(fromGuid, out var targets) ? [.. targets] : [];

    private static HashSet<string> Closure(Dictionary<string, List<string>> edges, string startGuid) =>
        Closure(edges, [startGuid]);

    private static HashSet<string> Closure(Dictionary<string, List<string>> edges, IReadOnlyCollection<string> startGuids)
    {
        var visited = new HashSet<string>(startGuids, StringComparer.Ordinal);
        var queue = new Queue<string>(startGuids);
        while (queue.TryDequeue(out var guid))
        {
            foreach (var next in DirectDeps(edges, guid).Where(visited.Add))
            {
                queue.Enqueue(next);
            }
        }

        return visited;
    }

    /// <summary>Physical Library/PackageCache/&lt;id&gt;@&lt;hash&gt;/… → Unity's virtual Packages/&lt;id&gt;/…</summary>
    private static string NormalizePackagePath(string path)
    {
        if (!path.StartsWith(PackageCachePrefix, StringComparison.Ordinal))
        {
            return path;
        }

        var rest = path[PackageCachePrefix.Length..];
        var at = rest.IndexOf('@');
        var slash = rest.IndexOf('/');
        return at < 0 || slash < 0 || at > slash
            ? path
            : "Packages/" + rest[..at] + rest[slash..];
    }

    private (Dictionary<string, string?> PathsByGuid, Dictionary<string, string> SourcesByGuid,
        Dictionary<string, List<string>> Edges) LoadGraph()
    {
        var paths = new Dictionary<string, string?>(StringComparer.Ordinal);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in GraphDatabaseTests.QueryStrings(fixture.Db,
                     "SELECT guid || '|' || coalesce(path, '') || '|' || source FROM assets"))
        {
            var parts = row.Split('|');
            paths[parts[0]] = parts[1].Length == 0 ? null : parts[1];
            sources[parts[0]] = parts[2];
        }

        // Code-layer kinds (Phase 2) are excluded: AssetDatabase.GetDependencies follows
        // serialized references only, never C# code, so code edges are intentionally
        // invisible to Unity and would otherwise show up as spurious "tool extras".
        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var row in GraphDatabaseTests.QueryStrings(fixture.Db,
                     $"SELECT DISTINCT from_guid || '|' || to_guid FROM asset_edges WHERE ref_kind NOT IN {CodeLayerFixtureTests.CodeKindsSql}"))
        {
            var parts = row.Split('|');
            if (!edges.TryGetValue(parts[0], out var targets))
            {
                edges[parts[0]] = targets = [];
            }

            targets.Add(parts[1]);
        }

        return (paths, sources, edges);
    }
}
