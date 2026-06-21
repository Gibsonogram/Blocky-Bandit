using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using static GridUtils;

public class Bishop : MonoBehaviour, ITurnActor, IGridActor, IPushable, IVisionSource
{
    private enum BishopState { Watch, Chase }

    [Header("Sprites")]
    [SerializeField] private Sprite watchSprite;
    [SerializeField] private Sprite chaseSprite;

    [Header("Idle Tween")]
    [SerializeField] private Transform visual;
    [SerializeField] private float tweenAmount = 0.1f;
    [SerializeField] private float tweenSpd = 1.5f;

    [Header("Settings")]
    [SerializeField] private float moveDur = 0.15f;
    [SerializeField] private int maxScanDistance = 40;
    [SerializeField] private int chaseDropDist = 3;

    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;
    private Vector2Int gridPosition;
    private BishopState state = BishopState.Watch;
    private Vector2Int lastKnownPlayerPos;
    private int lostSightTurns;

    public Vector2Int GridPosition => gridPosition;

    void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
    }

    void Start()
    {
        gridPosition = WorldToGrid(transform.position);
        rb.position = GridToWorld(gridPosition);
        TurnManager.Instance.RegisterActor(this);
        VisionOverlayRenderer.Instance.RegisterSource(this);
        StartCoroutine(IdleTween());
    }

    void OnDestroy()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.UnregisterActor(this);
        VisionOverlayRenderer.Instance?.UnregisterSource(this);
    }

    public void ExecutePush(Vector2Int direction)
    {
        Vector3 from = GridToWorld(gridPosition);
        gridPosition += direction;
        Vector3 to = GridToWorld(gridPosition);
        StartCoroutine(SmoothMove(from, to));
    }

    public void ExecuteBump(Vector2Int direction)
    {
        StartCoroutine(ActorUtils.BumpCoroutine(rb, gridPosition, direction, moveDur));
    }

    public bool OnPlayerMoveInto(Vector2Int direction)
    {
        PauseUI.Trigger(PauseContext.GameOver);
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

        if (state == BishopState.Watch)
        {
            if (canSee)
                EnterChase();
            return;
        }
        ChaseMove(lastKnownPlayerPos, canSee);
    }

    public IEnumerable<Vector2Int> GetVisibleTiles()
    {
        var tiles = new List<Vector2Int>();
        Vector2Int[] dirs = { new(1, 1), new(-1, 1), new(-1, -1), new(1, -1) };

        foreach (var dir in dirs)
        {
            for (int i = 1; i <= maxScanDistance; i++)
            {
                Vector2Int scan = gridPosition + (dir * i);
                IGridActor actor = QueryTile(scan, out bool isHardBlocked);

                if (isHardBlocked) break;
                if (actor != null && actor is not Collectable && actor != (IGridActor)this) break;

                tiles.Add(scan);
            }
        }
        return tiles;
    }

    bool HasLineOfSight(Vector2Int target)
    {
        Vector2Int delta = target - gridPosition;
        if (Mathf.Abs(delta.x) != Mathf.Abs(delta.y)) return false;

        Vector2Int dir = new Vector2Int((int)Mathf.Sign(delta.x), (int)Mathf.Sign(delta.y));

        for (int i = 1; i <= maxScanDistance; i++)
        {
            Vector2Int scan = gridPosition + (dir * i);
            if (scan == target) return true;

            IGridActor actor = QueryTile(scan, out bool isHardBlocked);
            if (isHardBlocked) break;
            if (actor != null && actor is not Collectable && actor != (IGridActor)this) break;
        }
        return false;
    }

    void ChaseMove(Vector2Int targetPos, bool canSee)
    {
        if (!canSee)
            lostSightTurns++;
        else
            lostSightTurns = 0;

        Vector2Int[] dirs = { new(1, 1), new(-1, 1), new(-1, -1), new(1, -1) };
        int currentSqrDist = (gridPosition - targetPos).sqrMagnitude;
        int bestSqrDist = currentSqrDist;
        List<Vector2Int> candidates = new List<Vector2Int>();

        foreach (var dir in dirs)
        {
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

                IGridActor actor = QueryTile(scan, out bool isHardBlocked);
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
        {
            MoveTo(candidates[Random.Range(0, candidates.Count)]);
            return;
        }

        if (lostSightTurns >= 2 && (gridPosition - targetPos).sqrMagnitude > chaseDropDist * chaseDropDist)
            ExitChase();
    }

    void MoveTo(Vector2Int target)
    {
        Vector3 from = GridToWorld(gridPosition);
        Vector3 to = GridToWorld(target);
        gridPosition = target;
        StartCoroutine(SmoothMove(from, to));
    }

    private void EnterChase()
    {
        state = BishopState.Chase;
        lostSightTurns = 0;
        spriteRenderer.sprite = chaseSprite;
    }

    private void ExitChase()
    {
        state = BishopState.Watch;
        lostSightTurns = 0;
        spriteRenderer.sprite = watchSprite;
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
        if (gridPosition == TurnManager.Instance.playerGridPosition)
            PauseUI.Trigger(PauseContext.GameOver);
    }

    private IEnumerator IdleTween()
    {
        while (true)
        {
            float offset = Mathf.Sin(Time.time * tweenSpd) * tweenAmount;
            visual.localPosition = new Vector3(0f, offset, 0f);
            yield return null;
        }
    }
}
