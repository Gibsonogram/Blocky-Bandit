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
    [Tooltip("Maximum reach of each arm in tiles. Large enough to cross any level; each " +
             "arm stops early at the first collision tile it meets.")]
    [SerializeField] private int beamLength = 60;

    [Header("Effect")]
    [Tooltip("Beam-shaped detonation prefab. Falls back to the plain burst when unset.")]
    [SerializeField] private GameObject beamEffectPrefab;

    // See BeamTiles(): invalidated by moving (pushed) or by a beamLength change.
    private HashSet<Vector2Int> cachedBeam;
    private Vector2Int cachedBeamOrigin;
    private int cachedBeamLength = -1;

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

        // Armed: the cross. BeamTiles clips each arm at the first collision tile, so the
        // cone and the blast effect both stop at walls.
        return BeamTiles();
    }

    protected override bool ResolveBlast()
    {
        return ResolveBlastAgainst(BeamTiles());
    }

    // The basic mine's burst only covers its own tile, but this blast spans the whole beam,
    // so the effect is told which tiles to draw on. The FX owns its own lifetime.
    protected override void SpawnExplosionEffect()
    {
        if (beamEffectPrefab == null)
        {
            base.SpawnExplosionEffect();
            return;
        }

        GameObject effect = Instantiate(beamEffectPrefab, GridUtils.GridToWorld(GridPosition), Quaternion.identity);
        if (effect.TryGetComponent(out MagicMineBoomFX boom))
            boom.Show(GridPosition, BeamTiles());
    }

    // Row and column through this mine, clipped by walls. Memoised because the result is a
    // pure function of the mine's tile and beamLength (collision tiles never change at
    // runtime), and it is read twice per detonation plus on every vision refresh. Without
    // the cache each armed mine re-walked 240 tiles and allocated a fresh set every turn.
    // Callers must treat the returned set as read-only.
    private HashSet<Vector2Int> BeamTiles()
    {
        if (cachedBeam != null && cachedBeamOrigin == GridPosition && cachedBeamLength == beamLength)
            return cachedBeam;

        var tiles = new HashSet<Vector2Int>();
        Vector2Int origin = GridPosition;

        AddBeamArm(tiles, origin, Vector2Int.right);
        AddBeamArm(tiles, origin, Vector2Int.left);
        AddBeamArm(tiles, origin, Vector2Int.up);
        AddBeamArm(tiles, origin, Vector2Int.down);

        cachedBeam = tiles;
        cachedBeamOrigin = origin;
        cachedBeamLength = beamLength;
        return cachedBeam;
    }

    private void AddBeamArm(HashSet<Vector2Int> tiles, Vector2Int origin, Vector2Int direction)
    {
        for (int i = 1; i <= beamLength; i++)
        {
            Vector2Int scan = origin + direction * i;
            // Same wall test the movement and vision systems use, so the beam agrees with
            // what can actually be walked through.
            if (GridUtils.IsCollisionTile(scan))
                return;

            tiles.Add(scan);
        }
    }
}