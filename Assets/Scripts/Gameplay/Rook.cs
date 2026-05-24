using UnityEngine;
using System.Collections;
using static GridUtils;
using System.Collections.Generic;

public class Rook : MonoBehaviour, ITurnActor, IGridActor, IPushable, IVisionSource
{
    private enum RookState { Watch, Chase }

    [Header("Sprites")]
    [SerializeField] private Sprite watchSprite;
    [SerializeField] private Sprite chaseSprite;

    [Header("Idle Tween")]
    [SerializeField] private Transform visual;
    [SerializeField] private float tweenAmount = 0.1f;
    [SerializeField] private float tweenSpd = 1.5f;

    [Header("Chase Settings")]
    [SerializeField] private float moveDur = 0.15f;
    [SerializeField] private int turnsToLoseChase = 2;

    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;
    private Vector2Int gridPosition;
    private RookState state = RookState.Watch;
    private int turnsSinceLostSight;
    private Vector2Int lastKnownPlayerPos;

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

    public bool TryGetPushed(Vector2Int direction)
    {
        Vector2Int targetPos = gridPosition + direction;
        QueryTile(targetPos, out bool isHardBlocked);
        if (isHardBlocked) return false;

        Vector3 from = GridToWorld(gridPosition);
        Vector3 to   = GridToWorld(targetPos);
        gridPosition = targetPos;
        StartCoroutine(SmoothMove(from, to));
        return true;
    }

    public bool OnPlayerMoveInto(Vector2Int direction)
    {
        // the rook catches the player — trigger game over
        PauseUI.Trigger(PauseContext.GameOver);
        return true;
    }

    public void TakeTurn()
    {
        Vector2Int playerPos = TurnManager.Instance.playerGridPosition;
        bool canSee = HasLineOfSight(playerPos);
        Debug.Log($"[Rook] TakeTurn - gridPos:{gridPosition} playerPos:{playerPos} canSee:{canSee} state:{state}");

        switch (state)
        {
            case RookState.Watch:
                if (canSee)
                {
                    lastKnownPlayerPos = playerPos;
                    EnterChase();
                }
                break;

            case RookState.Chase:
                if (canSee)
                {
                    lastKnownPlayerPos = playerPos;
                    turnsSinceLostSight = 0;
                }
                else
                {
                    turnsSinceLostSight++;
                    if (turnsSinceLostSight >= turnsToLoseChase)
                    {
                        EnterWatch();
                        return;
                    }
                }
                ChaseMove(lastKnownPlayerPos);
                break;
        }
    }

    public IEnumerable<Vector2Int> GetVisibleTiles()
    {
        var tiles = new List<Vector2Int>();
        Vector2Int[] axes = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        foreach (var ax in axes)
        {
            Vector2Int scan = gridPosition + ax;
            while (true)
            {
                IGridActor actor = QueryTile(scan, out bool isHardBlocked);
                if (isHardBlocked) break;
                if (actor != null && actor is not Collectable && actor != (IGridActor)this) break;
                tiles.Add(scan);
                scan += ax;
            }
        }
        return tiles;
    }

    bool HasLineOfSight(Vector2Int playerPos)
    {
        Vector2Int[] axes = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        foreach (var ax in axes)
        {
            Vector2Int scan = gridPosition + ax;
            while (true)
            {
                if (scan == playerPos) return true;
                IGridActor actor = QueryTile(scan, out bool isHardBlocked);
                if (isHardBlocked) break;
                if (actor != null && actor is not Collectable && actor != (IGridActor)this) break;
                scan += ax;
            }
        }
        return false;
    }

    void ChaseMove(Vector2Int targetPos)
    {
        Vector2Int delta = targetPos - gridPosition;
        int dx = Mathf.Abs(delta.x);
        int dy = Mathf.Abs(delta.y);

        if (dx == 0 && dy == 0) return;

        Vector2Int moveDir;
        if (dx > 0 && dy > 0)
            moveDir = Random.value < 0.5f
                ? new Vector2Int((int)Mathf.Sign(delta.x), 0)
                : new Vector2Int(0, (int)Mathf.Sign(delta.y));
        else if (dx > 0)
            moveDir = new Vector2Int((int)Mathf.Sign(delta.x), 0);
        else
            moveDir = new Vector2Int(0, (int)Mathf.Sign(delta.y));

        Vector2Int furthest = gridPosition;
        Vector2Int scan = gridPosition + moveDir;

        while (true)
        {
            if (scan == targetPos)
            {
                furthest = scan;
                break;
            }
            IGridActor actor = QueryTile(scan, out bool isHardBlocked);
            if (isHardBlocked || actor != null) break;
            furthest = scan;
            scan += moveDir;
        }

        if (furthest == gridPosition) return;

        Vector3 from = GridToWorld(gridPosition);
        Vector3 to = GridToWorld(furthest);
        gridPosition = furthest;
        StartCoroutine(SmoothMove(from, to));
    }

    private void EnterChase()
    {
        state = RookState.Chase;
        turnsSinceLostSight = 0;
        spriteRenderer.sprite = chaseSprite;
    }

    private void EnterWatch()
    {
        state = RookState.Watch;
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
        yield return new WaitForFixedUpdate();
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
