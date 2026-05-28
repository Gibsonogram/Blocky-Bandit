using UnityEngine;
using System.Collections;
using static GridUtils;

public class Crate : MonoBehaviour, IGridActor, IPushable
{
    public Vector2Int GridPosition => gridPosition;

    private static readonly int PushTrigger = Animator.StringToHash("Push");
    private const float MoveDuration = 0.15f;

    private Rigidbody2D rb;
    private Animator animator;
    private Vector2Int gridPosition;
    private bool isMoving;

    public bool OnPlayerMoveInto(Vector2Int direction) => TryPush(direction);

    private void Awake()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
    }

    private void Start()
    {
        gridPosition = WorldToGrid(transform.position);
        rb.position = GridToWorld(gridPosition);
    }

    bool TryPush(Vector2Int direction)
    {
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
        gridPosition += direction;
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
        if (animator != null) animator.SetTrigger(PushTrigger);

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
    }
}
