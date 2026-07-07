using System.Collections.Generic;
using UnityEngine;

// Static registry keyed by logical grid position. Holes are invisible to GridUtils.QueryTile
// (no collider, off ActorLayer) so actors detect them here rather than through physics.
public static class HoleRegistry
{
    private static readonly Dictionary<Vector2Int, Hole> Holes = new();

    public static void Register(Hole hole, Vector2Int gridPos)
    {
        Holes[gridPos] = hole;
    }

    public static void Unregister(Vector2Int gridPos)
    {
        Holes.Remove(gridPos);
    }

    public static bool TryGet(Vector2Int gridPos, out Hole hole)
    {
        return Holes.TryGetValue(gridPos, out hole);
    }

    // Steps one tile at a time from 'from' (exclusive) toward 'to' (inclusive) along the
    // straight cardinal/diagonal line and returns the first registered hole tile. For a
    // one-tile move this degenerates into a simple land-on check.
    public static bool FirstHoleOnPath(Vector2Int from, Vector2Int to, out Vector2Int holeTile, out Hole hole)
    {
        holeTile = default;
        hole = null;
        if (from == to) return false;

        Vector2Int step = new Vector2Int(
            (int)Mathf.Sign(to.x - from.x) * (to.x != from.x ? 1 : 0),
            (int)Mathf.Sign(to.y - from.y) * (to.y != from.y ? 1 : 0));

        Vector2Int current = from + step;
        while (true)
        {
            if (TryGet(current, out hole))
            {
                holeTile = current;
                return true;
            }
            if (current == to) break;
            current += step;
        }
        return false;
    }
}
