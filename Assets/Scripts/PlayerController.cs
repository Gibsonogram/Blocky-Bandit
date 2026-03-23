using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using static GridUtils;

public class PlayerController : MonoBehaviour
{
    [Header("Visual Smoothing")]
    [SerializeField] private float moveDuration = 0.15f;

    public event Action OnMovementComplete;
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    private Rigidbody2D rigidbody2d;
    private Animator animator;
    private Vector2Int gridPosition;
    private Vector2Int queuedDirection;
    public bool IsMoving { get; private set; }

    private void Awake()
    {
        rigidbody2d = GetComponent<Rigidbody2D>();
        animator = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        gridPosition = WorldToGrid(transform.position);
        rigidbody2d.position = GridToWorld(gridPosition);
    }

    public void TakeTurn()
    {
        if (queuedDirection == Vector2Int.zero || IsMoving) return;

        Vector2Int targetPos = gridPosition + queuedDirection;
        IGridActor actor = QueryTile(targetPos, out bool isHardBlocked);

        if (isHardBlocked) return;

        if (actor != null && !actor.OnPlayerMoveInto(queuedDirection)) return;

        Vector3 from = GridToWorld(gridPosition);
        Vector3 to = GridToWorld(targetPos);
        UpdateAnimator(queuedDirection);
        gridPosition = targetPos;
        queuedDirection = Vector2Int.zero;
        StartCoroutine(SmoothMove(from, to));
    }


    public void OnMove(InputValue value)
    {
        Vector2Int cardinal = SnapToCardinal(value.Get<Vector2>());

        // Only overwrite the buffer with a real direction — ignore zero (key-release)
        // events so they cannot wipe a buffered input mid-animation.
        if (cardinal != Vector2Int.zero)
            queuedDirection = cardinal;
    }


    private static Vector2Int SnapToCardinal(Vector2 input)
    {
        if (input == Vector2.zero) return Vector2Int.zero;

        if (Mathf.Abs(input.x) >= Mathf.Abs(input.y))
            return input.x > 0f ? Vector2Int.right : Vector2Int.left;
        else
            return input.y > 0f ? Vector2Int.up : Vector2Int.down;
    }

    private IEnumerator SmoothMove(Vector3 from, Vector3 to)
    {
        IsMoving = true;
        float elapsed = 0f;
        while (elapsed < moveDuration)
        {
            elapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);
            rigidbody2d.MovePosition(Vector3.Lerp(from, to, t));
            yield return new WaitForFixedUpdate();
        }

        rigidbody2d.MovePosition(to);
        IsMoving = false;
        if (queuedDirection == Vector2Int.zero)
        {
            animator.SetBool(IsMovingHash, false);
        }
        OnMovementComplete?.Invoke();
    }

    private void UpdateAnimator(Vector2Int direction)
    {
        if (animator == null) return;

        animator.SetFloat(MoveXHash, direction.x);
        animator.SetFloat(MoveYHash, direction.y);
        animator.SetBool(IsMovingHash, true);
    }
}
