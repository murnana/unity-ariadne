namespace GuidGraph.Graph;

/// <summary>How often an asset ends up in a scene or prefab once nested prefabs are expanded (N-8).</summary>
public sealed record ExpandedCountResult(
    long ExpandedCount, long Direct, long ViaNested, long Removed, long UnresolvedRemovals, bool DepthLimited,
    int ContainersReached);

/// <summary>
/// Counts objects that directly use an asset, multiplying through nested prefab placements:
/// a scene placing prefab P twice, where P holds 3 renderers using material M, has 6 of them.
/// Placements (and what instances removed) come from the placements tables, so no YAML is
/// opened here; only the removal check looks at refs/objects of the prefab that lost something.
/// </summary>
public static class ExpandedCounter
{
    // A PrefabInstance's own bookkeeping refs point at the prefab it instantiates, they are not uses
    // (same predicate as GraphWriter.RebuildPlacements).
    private const string UseOnlyPaths =
        "NOT (o.class_id = 1001 AND (r.property_path = 'm_SourcePrefab' OR r.property_path LIKE '%.target'"
        + " OR r.property_path LIKE 'm\\_Modification.m\\_Removed%' ESCAPE '\\'))";

    public static ExpandedCountResult Count(GraphDatabase db, string assetGuid, string containerGuid, int maxDepth)
    {
        maxDepth = Math.Clamp(maxDepth, 1, 50);

        // 1. How many copies of each container the scene holds, summed over all nesting paths.
        var copies = new Dictionary<string, long>(StringComparer.Ordinal) { [containerGuid] = 1 };
        var level = new Dictionary<string, long>(StringComparer.Ordinal) { [containerGuid] = 1 };
        var depthLimited = false;
        for (var depth = 0; level.Count > 0; depth++)
        {
            var next = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var (container, factor) in level)
            {
                foreach (var (child, instances) in Contained(db, container))
                {
                    next[child] = next.GetValueOrDefault(child) + factor * instances;
                }
            }

            if (next.Count > 0 && depth + 1 > maxDepth)
            {
                depthLimited = true;
                break;
            }

            foreach (var (child, count) in next)
            {
                copies[child] = copies.GetValueOrDefault(child) + count;
            }

            level = next;
        }

        // 2. Direct uses of the asset inside each container (use + contain), times its copies.
        long direct = 0;
        long viaNested = 0;
        foreach (var (container, count) in copies)
        {
            var uses = DirectUses(db, container, assetGuid);
            if (container == containerGuid)
            {
                direct += uses;
            }
            else
            {
                viaNested += count * uses;
            }
        }

        // 3. What PrefabInstances removed again (per instance, so times the container's copies).
        // A removed object whose id is not an object of the target prefab (Unity derives such ids for objects
        // that sit inside nested prefabs or models) cannot be followed; those are counted, not guessed.
        long removed = 0;
        long unresolved = 0;
        var cache = new Dictionary<(string, string, long), long?>();
        foreach (var (container, count) in copies)
        {
            foreach (var (target, kind, fileId) in Removals(db, container))
            {
                if (!cache.TryGetValue((target, kind, fileId), out var uses))
                {
                    uses = ObjectExists(db, target, fileId)
                        ? UsesInRemovedTarget(db, target, kind, fileId, assetGuid)
                        : null;
                    cache[(target, kind, fileId)] = uses;
                }

                if (uses is { } found)
                {
                    removed += count * found;
                }
                else
                {
                    unresolved += count;
                }
            }
        }

        var total = Math.Max(0, direct + viaNested - removed);
        return new ExpandedCountResult(total, direct, viaNested, removed, unresolved, depthLimited, copies.Count);
    }

    private static List<(string Guid, long Instances)> Contained(GraphDatabase db, string from)
    {
        var rows = new List<(string, long)>();
        using var command = db.Connection.CreateCommand();
        command.CommandText = "SELECT to_guid, instances FROM placements WHERE from_guid = $from AND role = 'contain'";
        command.Parameters.AddWithValue("$from", from);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return rows;
    }

    private static bool ObjectExists(GraphDatabase db, string asset, long fileId)
    {
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM objects o JOIN assets a ON a.id = o.asset_id WHERE a.guid = $asset AND o.file_id = $file LIMIT 1";
        command.Parameters.AddWithValue("$asset", asset);
        command.Parameters.AddWithValue("$file", fileId);
        return command.ExecuteScalar() is not null;
    }

    private static long DirectUses(GraphDatabase db, string from, string to)
    {
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(SUM(instances), 0) FROM placements WHERE from_guid = $from AND to_guid = $to";
        command.Parameters.AddWithValue("$from", from);
        command.Parameters.AddWithValue("$to", to);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static List<(string Target, string Kind, long FileId)> Removals(GraphDatabase db, string from)
    {
        var rows = new List<(string, string, long)>();
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            "SELECT to_guid, target_kind, target_file_id FROM placement_removals WHERE from_guid = $from";
        command.Parameters.AddWithValue("$from", from);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        }

        return rows;
    }

    /// <summary>
    /// Objects of the removed target (a component, or a GameObject with its components and, through
    /// m_Children, everything below it) that use the asset.
    /// </summary>
    private static long UsesInRemovedTarget(
        GraphDatabase db, string targetAsset, string kind, long fileId, string assetGuid)
    {
        var objects = new HashSet<long>();
        if (kind == "component")
        {
            objects.Add(fileId);
        }
        else
        {
            CollectGameObjectSubtree(db, targetAsset, fileId, objects, []);
        }

        long uses = 0;
        foreach (var objectFileId in objects)
        {
            using var command = db.Connection.CreateCommand();
            command.CommandText =
                $"""
                SELECT 1 FROM objects o JOIN assets a ON a.id = o.asset_id
                JOIN refs r ON r.from_object_id = o.id
                WHERE a.guid = $asset AND o.file_id = $file AND r.to_guid = $to
                  AND r.ref_kind <> 'corresponding_source' AND {UseOnlyPaths}
                LIMIT 1
                """;
            command.Parameters.AddWithValue("$asset", targetAsset);
            command.Parameters.AddWithValue("$file", objectFileId);
            command.Parameters.AddWithValue("$to", assetGuid);
            if (command.ExecuteScalar() is not null)
            {
                uses++;
            }
        }

        return uses;
    }

    /// <summary>The GameObject, its components (objects whose m_GameObject points at it) and its children's subtrees.</summary>
    private static void CollectGameObjectSubtree(
        GraphDatabase db, string asset, long gameObject, HashSet<long> objects, HashSet<long> visited)
    {
        if (!visited.Add(gameObject))
        {
            return;
        }

        foreach (var component in SameFileObjectsReferencing(db, asset, "m_GameObject", gameObject))
        {
            objects.Add(component);
            // A Transform lists its children's transforms; each child transform belongs to a child GameObject.
            foreach (var childTransform in SameFileTargetsOf(db, asset, component, "m_Children["))
            {
                foreach (var childGameObject in SameFileTargetsOf(db, asset, childTransform, "m_GameObject"))
                {
                    CollectGameObjectSubtree(db, asset, childGameObject, objects, visited);
                }
            }
        }
    }

    private static List<long> SameFileObjectsReferencing(GraphDatabase db, string asset, string path, long targetFileId)
    {
        var result = new List<long>();
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            """
            SELECT o.file_id FROM refs r JOIN objects o ON o.id = r.from_object_id JOIN assets a ON a.id = o.asset_id
            WHERE a.guid = $asset AND r.to_guid IS NULL AND r.property_path = $path AND r.to_file_id = $target
            """;
        command.Parameters.AddWithValue("$asset", asset);
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$target", targetFileId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetInt64(0));
        }

        return result;
    }

    private static List<long> SameFileTargetsOf(GraphDatabase db, string asset, long objectFileId, string pathPrefix)
    {
        var result = new List<long>();
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            """
            SELECT r.to_file_id FROM refs r JOIN objects o ON o.id = r.from_object_id JOIN assets a ON a.id = o.asset_id
            WHERE a.guid = $asset AND o.file_id = $file AND r.to_guid IS NULL AND r.property_path LIKE $prefix || '%'
            """;
        command.Parameters.AddWithValue("$asset", asset);
        command.Parameters.AddWithValue("$file", objectFileId);
        command.Parameters.AddWithValue("$prefix", pathPrefix);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetInt64(0));
        }

        return result;
    }
}
