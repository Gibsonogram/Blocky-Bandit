using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

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
    [SerializeField] private Vector2 gridOffset = new Vector2(0.5f, 0.5f);
    [SerializeField] private LayerMask collisionLayer;
    [SerializeField] private Tilemap collisionTilemap;


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

    private Rigidbody2D rigidbody2d;
    private Animator animator;
    private Vector2Int gridPosition;
    private Vector2Int queuedDirection;
    public bool IsMoving { get; private set; }

    private void Awake()
    {
        this.rigidbody2d = GetComponent<Rigidbody2D>();
        this.animator = GetComponentInChildren<Animator>();

        // Snap to the nearest grid cell on startup so the initial world
        // position is consistent with grid coordinates from frame one.
        this.gridPosition = WorldToGrid(transform.position);
        this.rigidbody2d.position = GridToWorld(this.gridPosition);
    }

    /// <summary>
    /// Called by TurnManager when it is the player's turn to act.
    /// Checks the target cell for colliders before committing the move.
    /// </summary>
    public void TakeTurn()
    {
        if (this.queuedDirection == Vector2Int.zero || IsMoving) return;

        Vector2Int targetPos = this.gridPosition + this.queuedDirection;

        // Check if that movement is valid.
        Vector3 targetWorldPos = GridToWorld(targetPos);
        Vector3Int tilePos = new Vector3Int(targetPos.x, targetPos.y, 0);
        if (this.collisionTilemap.HasTile(tilePos))
        {
            // Bump logic will go here.
            return;
        }

        Vector3 curWorldPos = GridToWorld(this.gridPosition);
        UpdateAnimator(this.queuedDirection);
        this.queuedDirection = Vector2Int.zero;

        this.gridPosition = targetPos;
        StartCoroutine(SmoothMove(curWorldPos, targetWorldPos));
    }

    /// <summary>Called by TurnManager when the Move action fires.</summary>
    public void OnMove(InputValue value)
    {
        Vector2Int cardinal = SnapToCardinal(value.Get<Vector2>());

        // Only overwrite the buffer with a real direction — ignore zero (key-release)
        // events so they cannot wipe a buffered input mid-animation.
        if (cardinal != Vector2Int.zero)
            this.queuedDirection = cardinal;
    }

    /// <summary>Converts a grid coordinate to a world position.</summary>
    private Vector3 GridToWorld(Vector2Int gridPos)
    {
        return new Vector3(
            gridPos.x * this.tileSize + this.gridOffset.x,
            gridPos.y * this.tileSize + this.gridOffset.y,
            0f);
    }

    /// <summary>Converts a world position to the nearest grid coordinate.</summary>
    private Vector2Int WorldToGrid(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.RoundToInt(worldPos.x / this.tileSize),
            Mathf.RoundToInt(worldPos.y / this.tileSize)
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
        while (elapsed < this.moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / this.moveDuration);
            this.rigidbody2d.MovePosition(Vector3.Lerp(from, to, t));
            yield return null;
        }

        // Always snap to the exact computed position — never a lerped float.
        this.rigidbody2d.MovePosition(to);
        IsMoving = false;

        // If a direction was buffered during the animation, notify TurnManager
        // so it can run a full turn cycle with the buffered input.
        if (this.queuedDirection != Vector2Int.zero)
            OnMovementComplete?.Invoke();
    }

    private void UpdateAnimator(Vector2Int direction)
    {
        if (this.animator == null) return;

        this.animator.SetFloat(MoveXHash, direction.x);
        this.animator.SetFloat(MoveYHash, direction.y);
        this.animator.SetBool(IsMovingHash, true);
    }
}
