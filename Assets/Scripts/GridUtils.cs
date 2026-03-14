using UnityEngine;
using UnityEngine.Tilemaps;

public static class GridUtils
{
    public const float TileSize = 2f;
    public static readonly Vector2 GridOffset = new Vector2(1f, 1f); // always TileSize / 2

    public static Vector3 GridToWorld(Vector2Int gridPos)
    {
        return new Vector3(
            gridPos.x * TileSize + GridOffset.x,
            gridPos.y * TileSize + GridOffset.y,
            0f);
    }

    public static Vector2Int WorldToGrid(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.RoundToInt((worldPos.x - GridOffset.x) / TileSize),
            Mathf.RoundToInt((worldPos.y - GridOffset.y) / TileSize));
    }

    // Checks static tilemap collision and dynamic actor on a target grid position.
    // Returns the IGridActor found, or null if the tile is free.
    // Sets isHardBlocked true if something is there with no IGridActor (impassable).
    public static IGridActor QueryTile(Vector2Int gridPos, Tilemap collisionTilemap, LayerMask actorLayer, out bool isHardBlocked)
    {
        Vector3Int tilePos = new Vector3Int(gridPos.x, gridPos.y, 0);
        if (collisionTilemap.HasTile(tilePos))
        {
            isHardBlocked = true;
            return null;
        }

        Vector3 worldPos = GridToWorld(gridPos);
        Collider2D hit = Physics2D.OverlapPoint(worldPos, actorLayer); // masked
        if (hit == null)
        {
            isHardBlocked = false;
            return null;
        }

        IGridActor actor = hit.GetComponent<IGridActor>();
        isHardBlocked = actor == null;
        return actor;
    }
}

