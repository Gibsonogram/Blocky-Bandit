using System.Collections.Generic;
using UnityEngine;

public static class GridUtils
{
    public static Vector3 GridToWorld(Vector2Int gridPos)
    {
        return new Vector3(
            gridPos.x * GridSettings.TileSize + GridSettings.GridOffset.x,
            gridPos.y * GridSettings.TileSize + GridSettings.GridOffset.y,
            0f);
    }

    public static Vector2Int WorldToGrid(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.RoundToInt((worldPos.x - GridSettings.GridOffset.x) / GridSettings.TileSize),
            Mathf.RoundToInt((worldPos.y - GridSettings.GridOffset.y) / GridSettings.TileSize));
    }

    public static IGridActor QueryTile(Vector2Int gridPos, out bool isHardBlocked)
    {
        if (IsCollisionTile(gridPos))
        {
            isHardBlocked = true;
            return null;
        }

        return QueryActorAtPosition(gridPos, out isHardBlocked, includeFinishTile: true);
    }

    public static IGridActor QueryActorTile(Vector2Int gridPos, out bool isHardBlocked)
    {
        if (IsCollisionTile(gridPos))
        {
            isHardBlocked = true;
            return null;
        }

        return QueryActorAtPosition(gridPos, out isHardBlocked, includeFinishTile: false);
    }

    public static bool IsFinishTile(Vector2Int gridPos)
    {
        return QueryFinishTile(gridPos) != null;
    }

    public static FinishTile QueryFinishTile(Vector2Int gridPos)
    {
        int hitCount = OverlapPoint(GridToWorld(gridPos), useActorLayer: false);
        for (int i = 0; i < hitCount; i++)
        {
            FinishTile finishTile = QueryResults[i].GetComponent<FinishTile>();
            if (finishTile != null && finishTile.isActiveAndEnabled)
                return finishTile;
        }

        return null;
    }

    public static void CheckForHoles(GameObject gameObject, Vector2Int gridPosition, Hole pendingHole)
    {
        if (pendingHole != null)
            pendingHole.Consume(gameObject, isPlayer: false);
        else if (HoleRegistry.TryGet(gridPosition, out Hole pushHole))
            pushHole.Consume(gameObject, isPlayer: false);
    }

    // Public so committed-tile reads (GridOccupancy) share this exact wall test.
    public static bool IsCollisionTile(Vector2Int gridPos)
    {
        Vector3Int tilePos = new Vector3Int(gridPos.x, gridPos.y, 0);
        return GridSettings.CollisionTilemap.HasTile(tilePos);
    }

    // Physics2D.OverlapPointAll allocates a fresh array on every call. These queries run in
    // tight loops - a MagicMine blast sweeps up to 240 tiles, and every mine re-scans its 8
    // neighbours each turn - so the results are collected into one reusable list instead.
    // Nothing here may call back into a query while the list is being read; all current
    // callers finish reading before their next query.
    private static readonly List<Collider2D> QueryResults = new();
    private static ContactFilter2D actorFilter;
    private static ContactFilter2D everythingFilter;
    private static bool actorFilterReady;

    private static void EnsureFilters()
    {
        if (actorFilterReady) return;

        // Mirrors OverlapPointAll: it honours the global trigger-query setting, so the
        // allocation-free path must too, or a crate/collectable authored as a trigger would
        // start (or stop) being hit when the project's physics settings change.
        bool hitTriggers = Physics2D.queriesHitTriggers;

        everythingFilter = ContactFilter2D.noFilter;
        everythingFilter.useTriggers = hitTriggers;

        actorFilter = ContactFilter2D.noFilter;
        actorFilter.useTriggers = hitTriggers;
        actorFilter.SetLayerMask(GridSettings.ActorLayer);

        actorFilterReady = true;
    }

    private static int OverlapPoint(Vector2 point, bool useActorLayer)
    {
        EnsureFilters();
        QueryResults.Clear();
        return Physics2D.OverlapPoint(point, useActorLayer ? actorFilter : everythingFilter, QueryResults);
    }

    // Allocation-free collider query on the actor layer, for callers that need to walk the
    // raw hits (GridOccupancy's committed-tile fallback). The results list is shared and only
    // valid until the next grid query, so read it before making another one.
    public static int OverlapActorColliders(Vector2 point, List<Collider2D> results)
    {
        EnsureFilters();
        results.Clear();
        return Physics2D.OverlapPoint(point, actorFilter, results);
    }

    private static IGridActor QueryActorAtPosition(Vector2Int gridPos, out bool isHardBlocked, bool includeFinishTile)
    {
        int hitCount = OverlapPoint(GridToWorld(gridPos), useActorLayer: true);
        bool hasNonActorCollider = false;
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hit = QueryResults[i];
            IGridActor actor = hit.GetComponent<IGridActor>();
            if (actor == null)
            {
                hasNonActorCollider = true;
                continue;
            }

            if (!includeFinishTile && actor is FinishTile)
                continue;

            isHardBlocked = false;
            return actor;
        }

        isHardBlocked = hasNonActorCollider;
        return null;
    }
}
