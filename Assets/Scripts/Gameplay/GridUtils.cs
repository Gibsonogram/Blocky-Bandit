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
        Collider2D[] hits = Physics2D.OverlapPointAll(GridToWorld(gridPos));
        foreach (Collider2D hit in hits)
        {
            FinishTile finishTile = hit.GetComponent<FinishTile>();
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

    private static bool IsCollisionTile(Vector2Int gridPos)
    {
        Vector3Int tilePos = new Vector3Int(gridPos.x, gridPos.y, 0);
        return GridSettings.CollisionTilemap.HasTile(tilePos);
    }

    private static IGridActor QueryActorAtPosition(Vector2Int gridPos, out bool isHardBlocked, bool includeFinishTile)
    {
        Collider2D[] hits = Physics2D.OverlapPointAll(GridToWorld(gridPos), GridSettings.ActorLayer);
        bool hasNonActorCollider = false;
        foreach (Collider2D hit in hits)
        {
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
