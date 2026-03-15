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

        Vector3Int tilePos = new Vector3Int(gridPos.x, gridPos.y, 0);
        if (GridSettings.CollisionTilemap.HasTile(tilePos))
        {
            isHardBlocked = true;
            return null;
        }

        Vector3 worldPos = GridToWorld(gridPos);
        Collider2D hit = Physics2D.OverlapPoint(worldPos, GridSettings.ActorLayer); // masked
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

