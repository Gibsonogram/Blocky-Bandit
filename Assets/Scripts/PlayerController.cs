using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles discrete tile-based top-down movement using the new Unity Input System.
/// Grid position is the sole source of truth — world position is always derived from it,
/// never read back from Unity, preventing floating point drift.
/// Requires a Kinematic Rigidbody2D and CapsuleCollider2D on the same GameObject.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CapsuleCollider2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Grid")]
    [SerializeField] private float tileSize = 1f;

    [Header("Visual Smoothing")]
    [SerializeField] private float moveDuration = 0.15f;

    /// <summary>
    /// Fired at the end of a movement animation when a buffered direction is waiting.
    /// TurnManager subscribes to this to trigger the next turn automatically.
    /// </summary>
    public event Action OnMovementComplete;

    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    private Rigidbody2D _rigidbody;
    private Animator _animator;

    // Integer grid position — the only authoritative position in the simulation.
    private Vector2Int _gridPosition;

    // Most recent input direction. Overwritten on every new input — only the latest is kept.
    private Vector2Int _queuedDirection;

    public bool IsMoving { get; private set; }

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody2D>();
        _animator = GetComponentInChildren<Animator>();

        // Snap to the nearest grid cell on startup so the initial world
        // position is consistent with grid coordinates from frame one.
        _gridPosition = WorldToGrid(transform.position);
        _rigidbody.position = GridToWorld(_gridPosition);
    }

    /// <summary>
    /// Called by TurnManager when it is the player's turn to act.
    /// Moves one tile in the queued direction, if any.
    /// </summary>
    public void TakeTurn()
    {
        if (_queuedDirection == Vector2Int.zero || IsMoving) return;

        Vector2Int targetGrid = _gridPosition + _queuedDirection;

        // TODO: Insert walkability / collision check here before committing the move.

        Vector3 from = GridToWorld(_gridPosition);
        _gridPosition = targetGrid;
        Vector3 to = GridToWorld(_gridPosition);

        UpdateAnimator(_queuedDirection);
        _queuedDirection = Vector2Int.zero;

        StartCoroutine(SmoothMove(from, to));
    }

    /// <summary>Called by the PlayerInput component when the Move action fires.</summary>
    public void OnMove(InputValue value)
    {
        _queuedDirection = SnapToCardinal(value.Get<Vector2>());
    }

    /// <summary>Converts a grid coordinate to a world position.</summary>
    private Vector3 GridToWorld(Vector2Int gridPos)
    {
        return new Vector3(gridPos.x * tileSize, gridPos.y * tileSize, 0f);
    }

    /// <summary>Converts a world position to the nearest grid coordinate.</summary>
    private Vector2Int WorldToGrid(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.RoundToInt(worldPos.x / tileSize),
            Mathf.RoundToInt(worldPos.y / tileSize)
        );
    }

    /// <summary>
    /// Collapses a raw Vector2 input into one of four cardinal directions.
    /// The dominant axis wins, preventing diagonal movement.
    /// </summary>
    private static Vector2Int SnapToCardinal(Vector2 input)
    {
        if (input == Vector2.zero) return Vector2Int.zero;

        if (Mathf.Abs(input.x) >= Mathf.Abs(input.y))
            return input.x > 0f ? Vector2Int.right : Vector2Int.left;
        else
            return input.y > 0f ? Vector2Int.up : Vector2Int.down;
    }

    /// <summary>
    /// Smoothly moves the visual representation from one tile to the next.
    /// The logical grid position is already updated before this runs —
    /// the lerp is purely cosmetic and always snaps to the exact derived position.
    /// </summary>
    private IEnumerator SmoothMove(Vector3 from, Vector3 to)
    {
        IsMoving = true;

        float elapsed = 0f;
        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);
            _rigidbody.MovePosition(Vector3.Lerp(from, to, t));
            yield return null;
        }

        // Always snap to the exact computed position — never a lerped float.
        _rigidbody.MovePosition(to);
        IsMoving = false;

        // If a direction was buffered during the animation, notify TurnManager
        // so it can run a full turn cycle with the buffered input.
        if (_queuedDirection != Vector2Int.zero)
            OnMovementComplete?.Invoke();
    }

    private void UpdateAnimator(Vector2Int direction)
    {
        if (_animator == null) return;

        _animator.SetFloat(MoveXHash, direction.x);
        _animator.SetFloat(MoveYHash, direction.y);
        _animator.SetBool(IsMovingHash, true);
    }
}
