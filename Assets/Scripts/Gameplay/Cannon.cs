using System.Collections.Generic;
using UnityEngine;
using static GridUtils;

// Cannon is a fixed hazard with no senses: it holds its tile and fires a single-tile-wide
// beam down its facing lane on every nth player turn. The two states are exactly that
// cadence - a counting turn, then a firing turn that presents the beam.
//
// It is level scenery rather than a combatant: it cannot be pushed, cannot be destroyed and
// therefore leaves no corpse. It commits its tile as a non-actor occupant (no IGridActor), so
// every mover - player, rook, bishop, queen and every push chain - is hard-blocked by it
// exactly as by a wall, and nothing can step onto its tile. Mine blasts sweep the tile and
// pass it over; another cannon blocks a beam outright.
//
// Facing is authored by rotating the prefab root: 0 degrees points right, and each +90
// degrees steps once counter-clockwise on screen (90 = up). The authored angle is snapped to
// the nearest quarter turn in Awake, so the sprite, the telegraphed lane and the beam can
// never disagree.
//
// The shot resolves inside TakeTurn. Bishops, queens and rooks (CombatPriority 0-1) have
// already committed their moves by then, so the beam reads final logical tiles; mines (5)
// tick afterwards, which is why a mine caught by a shell is flagged for chain detonation
// rather than destroyed outright - the same rule a mine caught in another mine's blast follows.
//
// Everything standing in the lane is destroyed - except another cannon, which is solid
// scenery and stops the shot: the player loses, turn actors are defeated and crates leave a
// corpse. Collectables are pickups, not targets, and are left alone so a lane crossing one
// cannot break the level's required total. The lane follows the exact rule MagicMine's arms
// use: it is walked outward one tile at a time and the first collision tile ends it, without
// being part of it. There is no per-instance range; the walk shares MagicMine's bound only to
// stay finite on an open map.
//
// The lane is also published as a vision source so the player can see the danger before it
// fires. That is presentation only - the cannon never detects, follows or aims at anything.
public class Cannon : MonoBehaviour, ITurnActor, IVisionSource, IIndestructible
{
    // After the movers (bishop/queen 0, rook 1) and before the mines (5), so the shot sees
    // every final logical position and a mine it hits is flagged before its own turn.
    public int CombatPriority => 4;

    // Same reach rule (and bound) as MagicMine: the lane runs until a collision tile stops it,
    // so there is no per-instance distance to tune.
    private const int BeamRange = MagicMine.DefaultBeamLength;

    [Header("Firing")]
    [Tooltip("Fire on every nth player turn. 1 = every turn, 2 = every other turn, ...")]
    [SerializeField, Min(1)] private int fireEveryNthTurn = 3;

    [Header("Effect")]
    [Tooltip("Beam prefab. MagicMineBoomFX draws a centre burst plus one strip per arm, so " +
             "feeding it only the forward tiles draws the single firing arm.")]
    [SerializeField] private GameObject beamEffectPrefab;

    [Tooltip("How long the Game Over screen waits so the beam reads before the panel opens.")]
    [SerializeField] private float defeatScreenDelay = 0.5f;

    [Header("Presentation")]
    [Tooltip("Draw the firing lane as a vision cone. Presentation only; the cannon never " +
             "detects anything.")]
    [SerializeField] private bool showFiringLane = true;

    // Reused by each shot so a firing turn allocates nothing while walking the actor list.
    private readonly List<ITurnActor> caughtActors = new();

    private Vector2Int gridPosition;
    private Rigidbody2D rb;
    // Authored facing, snapped to a quarter turn in Awake and cached for the cannon's life.
    private Vector2Int facingDirection;
    private Quaternion facingRotation = Quaternion.identity;
    // Built once: the cannon never moves and collision tiles are static at runtime.
    private HashSet<Vector2Int> lane;
    private int turnsSinceShot;

    public Vector2Int GridPosition => gridPosition;

    // Snaps an authored Z angle to the nearest quarter turn. Floor-based rather than
    // Mathf.RoundToInt so an exact halfway angle always resolves the same way.
    private static int QuarterTurns(float z)
    {
        return Mathf.FloorToInt((Mathf.Repeat(z, 360f) + 45f) / 90f) % 4;
    }

    private static Vector2Int DirectionForQuarterTurns(int quarterTurns)
    {
        switch (quarterTurns)
        {
            case 1: return Vector2Int.up;
            case 2: return Vector2Int.left;
            case 3: return Vector2Int.down;
            default: return Vector2Int.right;
        }
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        // Facing is authored by rotating the prefab root, so the sprite and the lane can never
        // drift apart the way a separate orientation field could.
        int quarterTurns = QuarterTurns(transform.eulerAngles.z);
        facingRotation = Quaternion.Euler(0f, 0f, quarterTurns * 90f);
        facingDirection = DirectionForQuarterTurns(quarterTurns);

        // Commit before any Start() runs: a neighbouring mine snapshots its baseline neighbour
        // hash in Start and must see this cannon holding its real tile.
        gridPosition = WorldToGrid(transform.position);
        GridOccupancy.Register(gridPosition, this);
    }

    private void Start()
    {
        // Snap onto the tile centre and the nearest quarter turn, so a cannon authored at an
        // odd angle still renders and shoots on the grid, like every other actor.
        if (rb != null)
        {
            rb.position = GridToWorld(gridPosition);
            rb.rotation = facingRotation.eulerAngles.z;
        }
        transform.rotation = facingRotation;

        TurnManager.Instance.RegisterActor(this);
        VisionOverlayRenderer.Instance?.RegisterSource(this);
    }

    private void OnDestroy()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.UnregisterActor(this);
        VisionOverlayRenderer.Instance?.UnregisterSource(this);
        GridOccupancy.Unregister(gridPosition, this);
    }

    // Part of the level, not a combatant: nothing can destroy it, so there is no defeat to
    // resolve. IIndestructible keeps it out of every blast and beam defeat path; this no-op
    // simply honours the ITurnActor contract.
    public void OnDefeat()
    {
    }

    public void TakeTurn()
    {
        turnsSinceShot++;
        if (turnsSinceShot < fireEveryNthTurn) return;

        turnsSinceShot = 0;
        Fire();
    }

    public IEnumerable<Vector2Int> GetVisibleTiles()
    {
        if (!showFiringLane) return System.Array.Empty<Vector2Int>();
        return LaneTiles();
    }

    private void Fire()
    {
        HashSet<Vector2Int> tiles = LaneTiles();
        SpawnBeamEffect(tiles);
        ResolveShot(tiles);
    }

    // The effect owns its own lifetime, like the mine's. The MagicMineBoomFX strips keep a
    // full-length lane to two renderers: the muzzle burst plus one tiled arm.
    private void SpawnBeamEffect(HashSet<Vector2Int> tiles)
    {
        if (beamEffectPrefab == null) return;

        GameObject effect = Instantiate(beamEffectPrefab, GridToWorld(gridPosition), Quaternion.identity);
        if (effect.TryGetComponent(out MagicMineBoomFX boom))
            boom.Show(gridPosition, tiles);
    }

    private void ResolveShot(HashSet<Vector2Int> tiles)
    {
        if (TurnManager.Instance == null) return;

        // Player caught in the lane locks in the loss. Presentation (hide player, corpse,
        // pause screen) waits on the beam so the effect isn't cut off mid-play.
        bool caughtPlayer = false;
        if (tiles.Contains(TurnManager.Instance.playerGridPosition))
            caughtPlayer = PauseUI.TryLockGameOver();

        // Snapshot first: OnDefeat unregisters and destroys while the actor list is read.
        caughtActors.Clear();
        foreach (ITurnActor actor in TurnManager.Instance.Actors)
        {
            if (ReferenceEquals(actor, this)) continue;
            // Fixed scenery is swept by the beam but survives it.
            if (actor is IIndestructible) continue;
            // An actor destroyed earlier this turn is still referenced until the frame ends.
            if (actor is Component component && component == null) continue;
            if (tiles.Contains(actor.GridPosition))
                caughtActors.Add(actor);
        }

        foreach (ITurnActor actor in caughtActors)
        {
            // A mine caught by the shell chain-detonates instead of vanishing, matching how a
            // mine caught in another mine's blast behaves.
            if (actor is Mine mine)
                mine.TriggerChainDetonation();
            else
                DefeatTurnActor(actor);
        }

        // Non-turn grid actors (crates, collectables) have no logical registry to walk. The
        // committed-tile read keeps a crate that is mid-push on the square it now owns.
        foreach (Vector2Int tile in tiles)
        {
            IGridActor actor = GridOccupancy.QueryActorTile(tile, out _);
            if (actor == null || actor is ITurnActor || actor is FinishTile || actor is Collectable)
                continue;

            if (actor is Crate crate)
                crate.OnDefeat();
            else if (actor is Component component)
                Destroy(component.gameObject);
        }

        if (caughtPlayer)
            PauseUI.ShowGameOverImmediateDefeat(defeatScreenDelay);
    }

    private static void DefeatTurnActor(ITurnActor actor)
    {
        // Clear the caught actor's cone this same turn, before its own cleanup runs.
        if (actor is IVisionSource visionSource)
            VisionOverlayRenderer.Instance?.UnregisterSource(visionSource);

        actor.OnDefeat();
    }

    // One tile wide, straight down the facing lane, stopping at the first collision tile or
    // another cannon. It never changes at runtime, so it is built once and shared by the
    // vision pass and every shot. Callers must treat the returned set as read-only.
    private HashSet<Vector2Int> LaneTiles()
    {
        if (lane != null) return lane;

        lane = new HashSet<Vector2Int>();
        Vector2Int direction = facingDirection;
        // Identical rule to MagicMine's arms: walk outward and stop at the first collision
        // tile, which is never part of the lane.
        for (int i = 1; i <= BeamRange; i++)
        {
            Vector2Int scan = gridPosition + direction * i;
            if (IsCollisionTile(scan)) break;

            // A cannon is solid scenery, so another one in the lane stops the shot before its
            // tile instead of being swept by it. Committed reads make this independent of the
            // colliders; only other indestructible scenery can block a beam.
            if (GridOccupancy.TryGet(scan, out Component occupant) && occupant is IIndestructible)
                break;

            lane.Add(scan);
        }
        return lane;
    }
}

