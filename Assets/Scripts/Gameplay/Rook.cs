using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using static GridUtils;

public class Rook : MonoBehaviour, ITurnActor, IGridActor, IPushable, IVisionSource, IExplosionReactor
{
    private enum RookState { Watch, Chase }

    public int CombatPriority => 1;

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

    [SerializeField] private GameObject corpsePrefab;

    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;
    private Vector2Int gridPosition;
    private RookState state = RookState.Watch;
    private Vector2Int lastKnownPlayerPos;

    public Vector2Int GridPosition => gridPosition;

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
        
        if (HasLineOfSight(playerPos))
            lastKnownPlayerPos = playerPos;

        if (state == RookState.Watch)
        {
            if (HasLineOfSight(playerPos))
            {
                EnterChase();
                return;
            }
            return;
        }

        ChaseMove(lastKnownPlayerPos);

        if (HasLineOfSight(playerPos))
            lastKnownPlayerPos = playerPos;
    }

    // Re-sense after a blast settles the board this turn. Updates sight state only; the
    // rook's move for this turn already happened in TakeTurn.
    public void ReactToExplosion()
    {
        Vector2Int playerPos = TurnManager.Instance.playerGridPosition;
        if (!HasLineOfSight(playerPos))
            return;

        lastKnownPlayerPos = playerPos;
        if (state == RookState.Watch)
            EnterChase();
    }

    public IEnumerable<Vector2Int> GetVisibleTiles()
    {
        var tiles = new List<Vector2Int>();
        Vector2Int[] axes = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        foreach (var ax in axes)
        {
            for (int i = 1; i <= maxScanDistance; i++)
            {
                Vector2Int scan = gridPosition + (ax * i);
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
        Vector2Int[] axes = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        foreach (var ax in axes)
        {
            for (int i = 1; i <= maxScanDistance; i++)
            {
                Vector2Int scan = gridPosition + (ax * i);
                if (scan == target) return true;

                IGridActor actor = QueryTile(scan, out bool isHardBlocked);
                if (isHardBlocked) break;
                if (actor != null && actor is not Collectable && actor != (IGridActor)this) break;
            }
        }
        return false;
    }

    void ChaseMove(Vector2Int targetPos)
    {
        Vector2Int delta = targetPos - gridPosition;
        if (delta == Vector2Int.zero) return;

        Vector2Int moveDir = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
            ? new Vector2Int((int)Mathf.Sign(delta.x), 0)
            : new Vector2Int(0, (int)Mathf.Sign(delta.y));

        Vector2Int furthest = gridPosition;
        for (int i = 1; i <= maxScanDistance; i++)
        {
            Vector2Int scan = gridPosition + (moveDir * i);
            if (moveDir.x != 0 && scan.x == targetPos.x) { furthest = scan; break; }
            if (moveDir.y != 0 && scan.y == targetPos.y) { furthest = scan; break; }

            IGridActor actor = QueryTile(scan, out bool isHardBlocked);
            if (isHardBlocked || (actor != null && actor is not Collectable && actor != (IGridActor)this)) break;

            furthest = scan;
        }

        if (furthest == gridPosition) return;

        Vector3 from = GridToWorld(gridPosition);
        Vector3 to = GridToWorld(furthest);
        gridPosition = furthest;

        // Resolve the catch synchronously here, inside the turn's actor loop, so the
        // player's committed position for this turn can't be dodged by a chained input
        // during the enemy's move tween.
        if (gridPosition == TurnManager.Instance.playerGridPosition)
            PauseUI.Trigger(PauseContext.GameOver);

        StartCoroutine(SmoothMove(from, to));
    }

    private void EnterChase()
    {
        state = RookState.Chase;
        spriteRenderer.sprite = chaseSprite;
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
