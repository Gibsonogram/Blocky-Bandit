using System.Collections.Generic;
using UnityEngine;

// MagicMine is a Mine whose blast is a cross instead of a ring.
//
// Detonation sweeps its entire row and column, so every actor that shares its horizontal
// or vertical at the moment of the blast is destroyed, however far away it is. Walls and
// crates do not shield the beam, matching the basic mine's no-line-of-sight ring; static
// tiles and holes are unaffected and simply let it pass.
//
// Everything else is inherited unchanged, so the ruleset is identical to the basic mine:
//   - it arms when its 8-neighbour ring changes, or when it is pushed;
//   - a hole voids the fuse and consumes it silently, with no detonation;
//   - it takes its turn last (CombatPriority 5) and resolves after every actor has moved;
//   - it chain-detonates when caught by another mine's blast, and its own blast chain-
//     detonates other mines;
//   - it is pushed and bumped like any pushable actor.
//
// The cone is inherited too: the ordinary ring while unarmed, then the beam cross the
// instant it arms, so what the player is shown is always the tile set that is lethal.
public class MagicMine : Mine
{
    [Header("Beam")]
    [Tooltip("Beam reach in each of the four directions, in tiles. Large enough to cross " +
             "any level; the blast itself is never blocked.")]
    [SerializeField] private int beamLength = 60;

    // The arming transition is the moment the lethal footprint changes from ring to cross,
    // so repaint the cone immediately instead of waiting for the next turn's refresh.
    protected override void EnterArmedState()
    {
        base.EnterArmedState();
        VisionOverlayRenderer.Instance?.Refresh();
    }

    public override IEnumerable<Vector2Int> GetVisibleTiles()
    {
        // Unarmed: the inherited ring, exactly like the basic mine.
        if (!IsArmed)
            return base.GetVisibleTiles();

        // Armed: the cross, drawn unfiltered. The beam is not stopped by walls or actors,
        // so filtering it through line of sight here would under-report the danger.
        return BeamTiles();
    }

    protected override bool ResolveBlast()
    {
        return ResolveBlastAgainst(BeamTiles());
    }

    // The full row and column through this mine. Its own tile is excluded, matching the
    // ring blast: nothing can be standing on a live mine, since walking into it is a loss.
    private HashSet<Vector2Int> BeamTiles()
    {
        var tiles = new HashSet<Vector2Int>();
        Vector2Int origin = GridPosition;

        for (int i = 1; i <= beamLength; i++)
        {
            tiles.Add(origin + new Vector2Int(i, 0));
            tiles.Add(origin + new Vector2Int(-i, 0));
            tiles.Add(origin + new Vector2Int(0, i));
            tiles.Add(origin + new Vector2Int(0, -i));
        }

        return tiles;
    }
}