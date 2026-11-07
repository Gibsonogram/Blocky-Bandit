using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using static GridUtils;

// The queen watches a short, ray-cast field around herself (a two tile radius along every
// queen line) and chases by sliding any distance along any of those eight lines, landing on
// the reachable tile that gets her closest to the player.
public class Queen : MonoBehaviour, ITurnActor, IGridActor, IPushable, IVisionSource, IExplosionReactor
{
    private enum QueenState { Watch, Chase }

    // Acts with the bishops, ahead of the rooks: the long-range pieces read the board first,
    // so a rook never chases toward a tile the queen has already committed away from.
    public int CombatPriority => 0;

    [Header("Sprites")]
    [SerializeField] private Sprite[] watchFrames; // queenWatch_0, queenWatch_1
    [SerializeField] private Sprite[] chaseFrames; // queenChase_0, queenChase_1
    [SerializeField] private float frameDuration = 0.5f;

    [Header("Settings")]
    [SerializeField] private float moveDur = 0.15f;
    // Sight reaches this many tiles along every ray. Wall/actor blocking truncates the ray, so
    // anything past a blocker is unseen even though it sits inside the radius.
    [SerializeField] private int visionRadius = 2;
    // Chase slides are unbounded on an open board, so this only caps a runaway scan.
    [SerializeField] private int maxScanDistance = 40;
    [SerializeField] private int chaseDropDist = 3;

    [SerializeField] private GameObject corpsePrefab;

    // Every queen line: the four cardinals and the four diagonals. Static so the per-turn sight
    // and chase scans don't allocate a fresh array on each call.
    private static readonly Vector2Int[] RayDirections =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(-1, 1), new(-1, -1), new(1, -1),
    };

    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;
    private Vector2Int gridPosition;
    private QueenState state = QueenState.Watch;
    private Vector2Int lastKnownPlayerPos;
    private int lostSightTurns;

    public Vector2Int GridPosition => gridPosition;

    void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        // Commit before any Start() runs, so a neighbouring mine's baseline hash (and any
        // other committed read) sees this queen at its real tile, not its collider pose.
        gridPosition = WorldToGrid(transform.position);
        GridOccupancy.Register(gridPosition, this);
    }

    void Start()
    {
        rb.position = GridToWorld(gridPosition);
        TurnManager.Instance.RegisterActor(this);
        VisionOverlayRenderer.Instance.RegisterSource(this);
        StartCoroutine(SpriteAnim());
    }

    void OnDestroy()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.UnregisterActor(this);
        VisionOverlayRenderer.Instance?.UnregisterSource(this);
        GridOccupancy.Unregister(gridPosition, this);
    }

    public void ExecutePush(Vector2Int direction)
    {
        Vector3 from = GridToWorld(gridPosition);
        Vector2Int fromTile = gridPosition;
        gridPosition += direction;
        GridOccupancy.Move(fromTile, gridPosition, this);
        Vector3 to = GridToWorld(gridPosition);
        StartCoroutine(SmoothMove(from, to));
    }

    public void ExecuteBump(Vector2Int direction)
    {
        StartCoroutine(ActorUtils.BumpCoroutine(rb, gridPosition, direction, moveDur));
    }

    public bool OnPlayerMoveInto(Vector2Int direction)
    {
        // Lock now so input freezes immediately; PlayerController presents the death
        // itself once its own walk-in animation lands on this tile.
        PauseUI.TryLockGameOver();
        return true;
    }

    public void TakeTurn()
    {
        Vector2Int playerPos = TurnManager.Instance.playerGridPosition;
        bool canSee = HasLineOfSight(playerPos);

        if (canSee)
            lastKnownPlayerPos = playerPos;
        else if (gridPosition == lastKnownPlayerPos)
            lastKnownPlayerPos = playerPos;

        if (state == QueenState.Watch)
        {
            if (canSee)
                EnterChase();
            return;
        }

        ChaseMove(lastKnownPlayerPos, playerPos, canSee);
    }

    // Re-sense after a blast settles the board this turn. Updates sight state only; the
    // queen's move for this turn already happened in TakeTurn.
    public void ReactToExplosion()
    {
        Vector2Int playerPos = TurnManager.Instance.playerGridPosition;
        bool canSee = HasLineOfSight(playerPos);

        if (canSee)
            lastKnownPlayerPos = playerPos;
        else if (gridPosition == lastKnownPlayerPos)
            lastKnownPlayerPos = playerPos;

        // A fresh sighting after a blast resets the cold-trail counter so the queen keeps
        // hunting instead of dropping out on a stale two-turn count.
        if (canSee)
            lostSightTurns = 0;

        if (state == QueenState.Watch && canSee)
            EnterChase();
    }

    // for ITurnActor
    public void OnDefeat()
    {
        // Freed now rather than at the deferred Destroy(): this turn's later reads (the
        // other actors' cone/hash scans) must not see a dead queen still holding its tile.
        GridOccupancy.Unregister(gridPosition, this);
        CombatEvents.RaiseDefeat(gridPosition, corpsePrefab);
        Destroy(gameObject);
    }

    public IEnumerable<Vector2Int> GetVisibleTiles()
    {
        var tiles = new List<Vector2Int>();

        foreach (var dir in RayDirections)
        {
            for (int i = 1; i <= visionRadius; i++)
            {
                Vector2Int scan = gridPosition + (dir * i);
                // Committed tiles, never the mid-tween colliders: see GridOccupancy.
                IGridActor actor = GridOccupancy.QueryActorTile(scan, out bool isHardBlocked);

                if (isHardBlocked) break;
                if (actor != null && actor is not Collectable && actor != (IGridActor)this) break;

                tiles.Add(scan);
            }
        }
        return tiles;
    }

    // Ray cast on the committed board: the player is seen only when it sits exactly on one of
    // the eight rays, inside visionRadius, with no wall or actor breaking the line first.
    bool HasLineOfSight(Vector2Int target)
    {
        Vector2Int delta = target - gridPosition;
        if (delta == Vector2Int.zero) return false;

        // System.Math.Sign, not (int)Mathf.Sign: Mathf.Sign(0) is 1, which would bend a
        // cardinal ray (a zero component) into a diagonal one.
        Vector2Int dir = new Vector2Int(System.Math.Sign(delta.x), System.Math.Sign(delta.y));
        int steps = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));

        // Not on a ray (e.g. a knight's hop away): nothing to walk, so nothing is seen.
        if (delta != dir * steps) return false;
        if (steps > visionRadius) return false;

        for (int i = 1; i <= steps; i++)
        {
            Vector2Int scan = gridPosition + (dir * i);
            if (scan == target) return true;

            IGridActor actor = GridOccupancy.QueryActorTile(scan, out bool isHardBlocked);
            if (isHardBlocked) break;
            if (actor != null && actor is not Collectable && actor != (IGridActor)this) break;
        }
        return false;
    }

    void ChaseMove(Vector2Int targetPos, Vector2Int playerPos, bool canSee)
    {
        if (!canSee)
            lostSightTurns++;
        else
            lostSightTurns = 0;

        // Give up the hunt once the trail goes cold: two consecutive turns without line of
        // sight while the player is more than chaseDropDist tiles away. Checked before the
        // move so the queen can't keep sliding toward a stale last known position forever.
        if (lostSightTurns >= 2 && (playerPos - gridPosition).sqrMagnitude > chaseDropDist * chaseDropDist)
        {
            ExitChase();
            return;
        }

        int currentSqrDist = (gridPosition - targetPos).sqrMagnitude;
        int bestSqrDist = currentSqrDist;
        List<Vector2Int> candidates = new List<Vector2Int>();

        foreach (var dir in RayDirections)
        {
            // She may travel any distance along a line, so the best tile in this direction is
            // the one closest to the target that this slide could actually reach.
            Vector2Int bestInDir = gridPosition;
            int bestInDirSqr = currentSqrDist;

            for (int i = 1; i <= maxScanDistance; i++)
            {
                Vector2Int scan = gridPosition + dir * i;

                if (scan == targetPos)
                {
                    bestInDir = scan;
                    bestInDirSqr = 0;
                    break;
                }

                // Landscape read from committed tiles: a crate that was pushed this turn
                // already blocks (or no longer blocks) its real tile.
                IGridActor actor = GridOccupancy.QueryActorTile(scan, out bool isHardBlocked);
                if (isHardBlocked) break;
                if (actor != null && actor is not Collectable) break;

                int sqrDist = (scan - targetPos).sqrMagnitude;
                if (sqrDist <= bestInDirSqr)
                {
                    bestInDirSqr = sqrDist;
                    bestInDir = scan;
                }
            }

            if (bestInDir == gridPosition) continue;

            if (bestInDirSqr < bestSqrDist)
            {
                bestSqrDist = bestInDirSqr;
                candidates.Clear();
                candidates.Add(bestInDir);
            }
            else if (bestInDirSqr == bestSqrDist)
            {
                candidates.Add(bestInDir);
            }
        }

        if (candidates.Count > 0)
            MoveTo(candidates[Random.Range(0, candidates.Count)]);
    }

    void MoveTo(Vector2Int target)
    {
        Vector2Int startPos = gridPosition;
        bool playerCatch = target == TurnManager.Instance.playerGridPosition;

        // A hole crossed along the slide truncates the move and consumes the queen, unless it
        // would catch the player on the same tile (catch wins).
        Hole pendingHole = null;
        if (HoleRegistry.FirstHoleOnPath(startPos, target, out Vector2Int holeTile, out Hole hole))
        {
            bool holeBeforePlayer = holeTile != target || !playerCatch;
            if (holeBeforePlayer)
            {
                target = holeTile;
                playerCatch = false;
                pendingHole = hole;
            }
        }

        Vector3 from = GridToWorld(startPos);
        Vector3 to = GridToWorld(target);
        GridOccupancy.Move(startPos, target, this);
        gridPosition = target;

        // Lock the catch synchronously here, inside the turn's actor loop, so the player's
        // committed position for this turn can't be dodged by a chained input during the
        // enemy's move tween. Presentation waits for the slide to land, but that wait is
        // registered on PauseUI rather than on this queen, so a mine blast destroying the
        // queen later this same turn can't cancel the kill it locked in.
        if (playerCatch && PauseUI.TryLockGameOver())
            PauseUI.ScheduleGameOverAfter(moveDur);

        StartCoroutine(SmoothMove(from, to, pendingHole));
    }

    private void EnterChase()
    {
        state = QueenState.Chase;
        lostSightTurns = 0;
    }

    private void ExitChase()
    {
        state = QueenState.Watch;
        lostSightTurns = 0;
    }

    private IEnumerator SmoothMove(Vector3 from, Vector3 to, Hole pendingHole = null)
    {
        ActorSorting.BeginStep(gameObject, from, to);
        float elapsed = 0f;
        while (elapsed < moveDur)
        {
            elapsed += Time.fixedDeltaTime;
            rb.MovePosition(Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / moveDur)));
            yield return new WaitForFixedUpdate();
        }
        rb.position = to;
        ActorSorting.EndStep(gameObject);
        GridUtils.CheckForHoles(gameObject, gridPosition, pendingHole);
    }

    // Loops the two-frame idle animation continuously, switching between the watch and chase
    // frame pairs based on state without interrupting the loop's timing.
    private IEnumerator SpriteAnim()
    {
        int frameIndex = 0;
        while (true)
        {
            Sprite[] frames = state == QueenState.Chase ? chaseFrames : watchFrames;
            spriteRenderer.sprite = frames[frameIndex % frames.Length];
            yield return new WaitForSeconds(frameDuration);
            frameIndex++;
        }
    }
}
