using UnityEngine;
using System.Collections;
using static GridUtils;

public class Rook : MonoBehaviour, ITurnActor
{
    private enum RookState { Watch, Chase }

    [Header("Sprites")]
    [SerializeField] private Sprite watchSprite;
    [SerializeField] private Sprite chaseSprite;

    [Header("Idle Tween")]
    [SerializeField] private Transform visual;       // assign the child sprite object
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
        StartCoroutine(IdleTween());
    }

    void OnDestroy()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.UnregisterActor(this);
    }

    public void TakeTurn()
    {
        Vector2Int playerPos = TurnManager.Instance.playerGridPosition;
        bool canSee = HasLineOfSight(playerPos);

        switch (state)
        {
            case RookState.Watch:
                if (canSee) EnterChase();
                break;

            case RookState.Chase:
                if (canSee)
                {
                    turnsSinceLostSight = 0;
                    ChaseMove(playerPos);
                }
                else
                {
                    turnsSinceLostSight++;
                    if (turnsSinceLostSight >= turnsToLoseChase)
                        EnterWatch();
                }
                break;
        }
    }

    bool HasLineOfSight(Vector2Int playerPos)
    {
        Vector2Int[] axes = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        foreach (var ax in axes)
        {
            Vector2Int scan = gridPosition + ax;
            while (true)
            {
                IGridActor actor = QueryTile(scan, out bool isHardBlocked);
                if (isHardBlocked) break;               // wall — stop
                if (scan == playerPos) return true;     // player found before any blocker
                if (actor != null) break;               // crate or other actor — stop
                scan += ax;
            }
        }
        return false;
    }

    void ChaseMove(Vector2Int playerPos)
    {
        Vector2Int delta = playerPos - gridPosition;
        int dx = Mathf.Abs(delta.x);
        int dy = Mathf.Abs(delta.y);

        Vector2Int moveDir;
        if (dx > 0 && dy > 0)
            moveDir = Random.value < 0.5f
                ? new Vector2Int((int)Mathf.Sign(delta.x), 0)
                : new Vector2Int(0, (int)Mathf.Sign(delta.y));
        else if (dx > 0)
            moveDir = new Vector2Int((int)Mathf.Sign(delta.x), 0);
        else
            moveDir = new Vector2Int(0, (int)Mathf.Sign(delta.y));

        Vector2Int targetPos = gridPosition + moveDir;
        QueryTile(targetPos, out bool isHardBlocked);
        if (isHardBlocked) return;

        if (targetPos == playerPos)
        {
            GameStateManager.Instance.ChangeState(GameState.EndScreen);
            return;
        }

        Vector3 from = GridToWorld(gridPosition);
        Vector3 to = GridToWorld(targetPos);
        gridPosition = targetPos;
        StartCoroutine(SmoothMove(from, to));
    }

    private void EnterChase()
    {
        state = RookState.Chase;
        turnsSinceLostSight = 0;
        spriteRenderer.sprite = chaseSprite;
        ChaseMove(TurnManager.Instance.playerGridPosition);
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
        rb.MovePosition(to);
    }

    private IEnumerator IdleTween()
    {
        // runs on the child visual, so rb.MovePosition on the root never conflicts
        while (true)
        {
            float offset = Mathf.Sin(Time.time * tweenSpd) * tweenAmount;
            visual.localPosition = new Vector3(0f, offset, 0f);
            yield return null;
        }
    }
}
