using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using static GridUtils;

public class Mine : MonoBehaviour, ITurnActor, IGridActor, IPushable, IVisionSource
{
    private enum MineState { Unarmed, Armed }

    // Mine should have the lowest priority. It blows at the END of actor moves.
    public int CombatPriority => 5;

    [Header("Sprites")]
    [SerializeField] private GameObject corpsePrefab;

    [Header("Countdown")]
    [SerializeField] private TMP_Text timerText;

    [Header("Explosion")]
    [SerializeField] private GameObject explosionEffectPrefab;
    [SerializeField] private float explosionPresentationDelay = 0.5f;

    private const int ArmedTimerStart = 3;
    // Indexable by mineTimer to avoid per-turn string allocations.
    private static readonly string[] TimerLabels = { "0", "1", "2", "3" };

    private int mineTimer = ArmedTimerStart;
    private int baselineNeighborHash;
    private bool isExploding;
    private bool hasResolvedDetonation;
    // Pushed onto a hole: the mine is consumed, so its fuse is void — it never detonates.
    private bool consumedByHole;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;
    private Vector2Int gridPosition;
    private MineState state = MineState.Unarmed;
    private float moveDur = 0.15f;
    private bool wasMoved = false;

    public Vector2Int GridPosition => gridPosition;

    // Exposed to subclasses so they can present a different cone per state: MagicMine
    // shows the ordinary ring while unarmed and its cross once armed.
    protected bool IsArmed => state == MineState.Armed;

    // Set the turn the fuse hits zero. TurnManager resolves flagged mines after all
    // actors have moved this turn, so the blast lands on final logical positions.
    public bool IsPendingDetonation { get; private set; }
    private static readonly Vector2Int[] NeighborOffsets =
    {
        new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(0, 1), new Vector2Int(-1, 1), 
        new Vector2Int(-1, 0), new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
    };

    void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        // Commit before any Start() runs: the baseline fuse hash is snapshotted in Start,
        // and it must record this mine (and its neighbours) at their real tiles rather than
        // at whatever pose the colliders happened to be authored or ordered in.
        gridPosition = WorldToGrid(transform.position);
        GridOccupancy.Register(gridPosition, this);
    }

    public void OnDefeat()
    {
        // Freed now rather than at the deferred Destroy(): this turn's later reads (the
        // other actors' cone/hash scans) must not see a dead mine still holding its tile.
        GridOccupancy.Unregister(gridPosition, this);
        CombatEvents.RaiseDefeat(gridPosition, corpsePrefab);
        Destroy(gameObject);
    }

    void Start()
    {
        rb.position = GridToWorld(gridPosition);
        TurnManager.Instance.RegisterActor(this);
        VisionOverlayRenderer.Instance.RegisterSource(this);

        baselineNeighborHash = ComputeNeighborHash();
        if (timerText != null)
            timerText.enabled = false;
    }

    void OnDestroy()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.UnregisterActor(this);
        VisionOverlayRenderer.Instance?.UnregisterSource(this);
        GridOccupancy.Unregister(gridPosition, this);
    }

    public void ExecuteBump(Vector2Int direction)
    {
        StartCoroutine(ActorUtils.BumpCoroutine(rb, gridPosition, direction, moveDur));
    }


    public void ExecutePush(Vector2Int direction)
    {
        Vector3 from = GridToWorld(gridPosition);
        Vector2Int fromTile = gridPosition;
        gridPosition += direction;
        GridOccupancy.Move(fromTile, gridPosition, this);
        Vector3 to = GridToWorld(gridPosition);
        StartCoroutine(SmoothMove(from, to));
        wasMoved = true;
    }

    private IEnumerator SmoothMove(Vector3 from, Vector3 to)
    {
        float elapsed = 0f;
        while (elapsed < moveDur)
        {
            elapsed += Time.fixedDeltaTime;
            rb.MovePosition(Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / moveDur)));
            yield return new WaitForFixedUpdate();
        }
        rb.position = to;

        // Pushed onto a hole: consumed silently — no detonation, no blast, no corpse.
        if (HoleRegistry.TryGet(gridPosition, out Hole hole))
        {
            // Void the fuse before the consume so this same turn's actor loop can't flag a
            // detonation for a mine that is already gone down the hole.
            MarkConsumedByHole();
            hole.Consume(gameObject, isPlayer: false);
        }
    }

    public bool OnPlayerMoveInto(Vector2Int direction) => TryPush(direction);

    private bool TryPush(Vector2Int direction)
    {
        if (!ActorUtils.TryResolvePush(gridPosition, direction, out _))
        {
            ExecuteBump(direction);
            return false;
        }

        ExecutePush(direction);
        return true;
    }

    public void TakeTurn()
    {
        if (isExploding || consumedByHole)
            return;

        if (state == MineState.Unarmed)
        {
            UnarmedMove();
            return;
        }

        ArmedMove();
    }

    public virtual IEnumerable<Vector2Int> GetVisibleTiles()
    {
        var tiles = new List<Vector2Int>();
        foreach (Vector2Int offset in NeighborOffsets)
        {
            Vector2Int scan = gridPosition + offset;
            // Committed tiles, never the mid-tween colliders: see GridOccupancy.
            IGridActor actor = GridOccupancy.QueryActorTile(scan, out bool isHardBlocked);
            if (isHardBlocked) continue;
            if (actor != null && actor is not Collectable && actor != (IGridActor)this) continue;
            tiles.Add(scan);
        }
        return tiles;
    }

    private void ArmedMove()
    {
        mineTimer -= 1;
        if (mineTimer < 1)
        {
            FlagDetonation();
            return;
        }
        if (timerText != null)
            timerText.text = TimerLabels[mineTimer];
    }

    // Re-scans the 8 neighbors each unarmed turn. Any change from the initial
    // baseline (an actor entering or leaving proximity) arms the mine.
    private void UnarmedMove()
    {
        if (ComputeNeighborHash() == baselineNeighborHash && !wasMoved)
            return;
        EnterArmedState();
    }

    // Virtual so a subclass whose cone depends on state (MagicMine) can repaint it the
    // moment the footprint changes, rather than one turn late.
    protected virtual void EnterArmedState()
    {
        state = MineState.Armed;
        mineTimer = ArmedTimerStart;
        if (timerText != null)
        {
            timerText.enabled = true;
            timerText.text = TimerLabels[mineTimer];
        }
    }

    private int ComputeNeighborHash()
    {
        int hash = 17;
        foreach (Vector2Int offset in NeighborOffsets)
        {
            Vector2Int scan = gridPosition + offset;
            // Committed tiles: an actor or crate that moved this turn is already at its new
            // tile, so the fuse starts ticking on the turn the cone actually changed.
            IGridActor actor = GridOccupancy.QueryActorTile(scan, out bool isHardBlocked);
            // 0 = empty, 1 = hard blocked (static walls), 2 = occupied by an actor.
            int tileState = isHardBlocked ? 1 : (actor != null ? 2 : 0);
            hash = hash * 31 + tileState;
        }
        return hash;
    }

    private void FlagDetonation()
    {
        if (consumedByHole)
            return;

        // Standing on a hole tile means the mine has been (or is being) consumed: the fuse
        // is void, even if the landing tween hasn't finished this same turn.
        if (HoleRegistry.TryGet(gridPosition, out _))
        {
            MarkConsumedByHole();
            return;
        }

        isExploding = true;
        IsPendingDetonation = true;
        if (timerText != null)
            timerText.enabled = false;

        // Unregister the vision source now so TurnManager's Refresh() this same turn no
        // longer includes this mine's tiles. TurnManager resolves the blast after every
        // actor has moved, then refreshes once against the settled board.
        VisionOverlayRenderer.Instance?.UnregisterSource(this);
    }

    // Consumed by a hole: the fuse is void. Clears any pending flag so TurnManager's drain
    // loop skips this mine, and marks it resolved so a chained blast can't re-arm it.
    private void MarkConsumedByHole()
    {
        consumedByHole = true;
        IsPendingDetonation = false;
        hasResolvedDetonation = true;
        if (timerText != null)
            timerText.enabled = false;
    }

    // Caught in another mine's blast: detonate immediately regardless of current state,
    // skipping the countdown. Flags for the drain loop; guarded against re-flagging a
    // mine that already resolved or is already pending this turn.
    public void TriggerChainDetonation()
    {
        if (consumedByHole || hasResolvedDetonation || IsPendingDetonation)
            return;
        FlagDetonation();
    }

    // Called by TurnManager after all actors have taken their turn this frame. Resolves
    // the blast against final logical positions and destroys the mine. Guarded so a mine
    // caught in another mine's blast can't resolve twice.
    public void ResolveDetonation()
    {
        if (hasResolvedDetonation)
            return;
        hasResolvedDetonation = true;
        IsPendingDetonation = false;

        // The effect prefab manages its own duration and cleanup.
        SpawnExplosionEffect();

        bool caughtPlayer = ResolveBlast();
        CombatEvents.RaiseDefeat(gridPosition, corpsePrefab);
        // Corpse and player visual swap happen immediately alongside the blast; only the
        // pause screen itself waits, so it doesn't cut off the explosion FX mid-play.
        if (caughtPlayer)
            PauseUI.ShowGameOverImmediateDefeat(explosionPresentationDelay);
        Destroy(gameObject);
    }

    // Spawns the detonation effect. Virtual because the footprint differs: the basic mine's
    // effect is a small burst on its own tile, while MagicMine draws across its whole beam.
    protected virtual void SpawnExplosionEffect()
    {
        if (explosionEffectPrefab != null)
            Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
    }

    // The blast footprint. The basic mine sweeps a fixed 8-tile ring with no line of sight;
    // MagicMine replaces this with its cross. Both footprints resolve through
    // ResolveBlastAgainst, so the kill / chain / crate rules live in exactly one place.
    protected virtual bool ResolveBlast()
    {
        var blastTiles = new HashSet<Vector2Int>();
        foreach (Vector2Int offset in NeighborOffsets)
            blastTiles.Add(gridPosition + offset);

        return ResolveBlastAgainst(blastTiles);
    }

    // Resolves a blast over an arbitrary set of tiles against final logical positions,
    // killing caught actors, chain-detonating caught mines and clearing caught crates.
    // Returns true if the player was caught, so the caller can schedule the Game Over
    // presentation to wait for the explosion effect.
    protected bool ResolveBlastAgainst(ICollection<Vector2Int> blastTiles)
    {
        bool caughtPlayer = false;
        if (TurnManager.Instance != null)
        {
            // Player caught in the blast locks in the loss. Presentation (hide player,
            // corpse, pause screen) is deferred until the explosion effect finishes.
            if (blastTiles.Contains(TurnManager.Instance.playerGridPosition))
                caughtPlayer = PauseUI.TryLockGameOver();

            // Turn actors: compare final logical positions. Their colliders may still be
            // mid-move this turn, so a physics query would miss actors that just stepped in.
            var caught = new List<ITurnActor>();
            foreach (ITurnActor actor in TurnManager.Instance.Actors)
            {
                if (ReferenceEquals(actor, this))
                    continue;
                if (blastTiles.Contains(actor.GridPosition))
                    caught.Add(actor);
            }
            foreach (ITurnActor actor in caught)
            {
                // A mine caught in the blast chain-detonates: it skips its countdown and
                // is flagged for immediate resolution by TurnManager's drain loop, which
                // lets its own blast propagate to further mines this same turn.
                if (actor is Mine otherMine)
                    otherMine.TriggerChainDetonation();
                else
                    DefeatTurnActor(actor);
            }
        }

        // Non-turn grid actors (e.g. crates) have no turn/logical registry; resolve them
        // via physics, which is settled since they only move on player pushes.
        foreach (Vector2Int tile in blastTiles)
        {
            IGridActor actor = QueryTile(tile, out _);
            if (actor == null || actor is ITurnActor)
                continue;
            if (actor is FinishTile) 
                continue;
            if (actor is Crate crate)
                crate.OnDefeat();
            else if (actor is Component component)
                Destroy(component.gameObject);
        }

        return caughtPlayer;
    }

    private void DefeatTurnActor(ITurnActor actor)
    {
        // Clear any vision cone belonging to the caught actor this same turn too.
        if (actor is IVisionSource visionSource)
            VisionOverlayRenderer.Instance?.UnregisterSource(visionSource);

        actor.OnDefeat();
    }
}
