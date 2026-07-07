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
    [SerializeField] private TextMeshPro timerText;

    [Header("Explosion")]
    [SerializeField] private GameObject explosionEffectPrefab;

    private const int ArmedTimerStart = 3;
    // Indexable by mineTimer to avoid per-turn string allocations.
    private static readonly string[] TimerLabels = { "0", "1", "2", "3" };

    private int mineTimer = ArmedTimerStart;
    private int baselineNeighborHash;
    private bool isExploding;
    private bool hasResolvedDetonation;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;
    private Vector2Int gridPosition;
    private MineState state = MineState.Unarmed;
    private float moveDur = 0.15f;

    public Vector2Int GridPosition => gridPosition;

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
    }

    public void OnDefeat()
    {
        CombatEvents.RaiseDefeat(gridPosition, corpsePrefab);
        Destroy(gameObject);
    }

    void Start()
    {
        gridPosition = WorldToGrid(transform.position);
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
    }

    public void ExecuteBump(Vector2Int direction)
    {
        StartCoroutine(ActorUtils.BumpCoroutine(rb, gridPosition, direction, moveDur));
    }


    public void ExecutePush(Vector2Int direction)
    {
        Vector3 from = GridToWorld(gridPosition);
        gridPosition += direction;
        Vector3 to = GridToWorld(gridPosition);
        StartCoroutine(SmoothMove(from, to));
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
            hole.Consume(gameObject, isPlayer: false);
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
        if (isExploding)
            return;

        if (state == MineState.Unarmed)
        {
            UnarmedMove();
            return;
        }

        ArmedMove();
    }

    public IEnumerable<Vector2Int> GetVisibleTiles()
    {
        var tiles = new List<Vector2Int>();
        foreach (Vector2Int offset in NeighborOffsets)
        {
            Vector2Int scan = gridPosition + offset;
            IGridActor actor = QueryTile(scan, out bool isHardBlocked);
            if (isHardBlocked) continue;
            if (actor != null && actor is not Collectable && actor != (IGridActor)this) continue;
            tiles.Add(scan);
        }
        return tiles;
    }

        void ArmedMove()
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
        if (ComputeNeighborHash() == baselineNeighborHash)
            return;

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
            IGridActor actor = QueryTile(scan, out bool isHardBlocked);
            // 0 = empty, 1 = hard blocked (static walls), 2 = occupied by an actor.
            int tileState = isHardBlocked ? 1 : (actor != null ? 2 : 0);
            hash = hash * 31 + tileState;
        }
        return hash;
    }

    private void FlagDetonation()
    {
        isExploding = true;
        IsPendingDetonation = true;
        if (timerText != null)
            timerText.enabled = false;

        // Unregister the vision source now so TurnManager's Refresh() this same turn no
        // longer includes this mine's tiles. TurnManager resolves the blast after every
        // actor has moved, then refreshes once against the settled board.
        VisionOverlayRenderer.Instance?.UnregisterSource(this);
    }

    // Caught in another mine's blast: detonate immediately regardless of current state,
    // skipping the countdown. Flags for the drain loop; guarded against re-flagging a
    // mine that already resolved or is already pending this turn.
    public void TriggerChainDetonation()
    {
        if (hasResolvedDetonation || IsPendingDetonation)
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
        if (explosionEffectPrefab != null)
            Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);

        ResolveBlast();
        CombatEvents.RaiseDefeat(gridPosition, corpsePrefab);
        Destroy(gameObject);
    }

    private void ResolveBlast()
    {
        var blastTiles = new HashSet<Vector2Int>();
        foreach (Vector2Int offset in NeighborOffsets)
            blastTiles.Add(gridPosition + offset);

        if (TurnManager.Instance != null)
        {
            // Player caught in the blast triggers game over.
            if (blastTiles.Contains(TurnManager.Instance.playerGridPosition))
                PauseUI.Trigger(PauseContext.GameOver);

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
            if (actor is FinishTile) { continue; } // guard against destroying finish! 
                continue;
            if (actor is Crate crate)
                crate.OnDefeat();
            else if (actor is Component component)
                Destroy(component.gameObject);
        }
    }

    private void DefeatTurnActor(ITurnActor actor)
    {
        // Clear any vision cone belonging to the caught actor this same turn too.
        if (actor is IVisionSource visionSource)
            VisionOverlayRenderer.Instance?.UnregisterSource(visionSource);

        actor.OnDefeat();
    }
}
