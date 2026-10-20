using UnityEngine;
using System.Collections;
using static GridUtils;

public class Crate : MonoBehaviour, IGridActor, IPushable
{
    public Vector2Int GridPosition => gridPosition;

    private static readonly int PushTrigger = Animator.StringToHash("Push");
    private const float MoveDuration = 0.15f;

    [SerializeField] private GameObject corpsePrefab;

    private Rigidbody2D rb;
    private Animator animator;
    private Vector2Int gridPosition;
    private bool isMoving;

    public bool OnPlayerMoveInto(Vector2Int direction) => TryPush(direction);

    public void OnDefeat()
    {
        // Freed now rather than at the deferred Destroy(): this turn's later reads (the
        // enemy cone/hash scans) must not see a destroyed crate still holding its tile.
        GridOccupancy.Unregister(gridPosition, this);
        CombatEvents.RaiseDefeat(gridPosition, corpsePrefab);
        Destroy(gameObject);
    }

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody2D>();
        // Crates are landscape for every enemy/hazard read: commit before any Start() runs
        // so a mine's baseline hash records them at their real tiles.
        gridPosition = WorldToGrid(transform.position);
        GridOccupancy.Register(gridPosition, this);
    }

    private void Start()
    {
        rb.position = GridToWorld(gridPosition);
    }

    bool TryPush(Vector2Int direction)
    {
        if (isMoving) return false;

        if (!ActorUtils.TryResolvePush(gridPosition, direction, out _))
        {
            ExecuteBump(direction);
            return false;
        }

        ExecutePush(direction);
        return true;
    }

    public void ExecutePush(Vector2Int direction)
    {
        Vector3 from = GridToWorld(gridPosition);
        Vector2Int fromTile = gridPosition;
        gridPosition += direction;
        GridOccupancy.Move(fromTile, gridPosition, this);
        Vector3 to = GridToWorld(gridPosition);
        StartCoroutine(PushRoutine(from, to));
    }

    public void ExecuteBump(Vector2Int direction)
    {
        StartCoroutine(ActorUtils.BumpCoroutine(rb, gridPosition, direction, MoveDuration));
    }

    private IEnumerator PushRoutine(Vector3 from, Vector3 to)
    {
        isMoving = true;
        if (animator != null && animator.runtimeAnimatorController != null) 
            animator.SetTrigger(PushTrigger);

        float elapsed = 0f;
        while (elapsed < MoveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / MoveDuration);
            rb.MovePosition(Vector3.Lerp(from, to, t));
            yield return null;
        }

        rb.MovePosition(to);
        isMoving = false;

        // Pushed onto a hole: consumed silently (no corpse).
        if (HoleRegistry.TryGet(gridPosition, out Hole hole))
            hole.Consume(gameObject, isPlayer: false);
    }

    private void OnDestroy()
    {
        GridOccupancy.Unregister(gridPosition, this);
    }

}
