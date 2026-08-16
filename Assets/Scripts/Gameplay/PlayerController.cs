using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using static GridUtils;

public class PlayerController : MonoBehaviour
{
    [Header("Visual Smoothing")]
    [SerializeField] private float moveDuration = 0.15f;

    [Header("Defeat")]
    [SerializeField] private GameObject visual;
    [SerializeField] private GameObject corpsePrefab;
    
    public Vector2Int playerGridPosition;
    public event Action OnMovementComplete;
    
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private Rigidbody2D rigidbody2d;
    private Animator animator;
    private Vector2Int queuedDirection;
    private Hole pendingHole;
    private bool isDead;
    public bool IsMoving { get; private set; }

    private void Awake()
    {
        rigidbody2d = GetComponent<Rigidbody2D>();
        animator = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        TurnManager.Instance.RegisterPlayer(this);
        if (StartTile.Instance != null)
        {
            playerGridPosition = WorldToGrid(StartTile.Instance.transform.position);
        }
        else
        {
            playerGridPosition = WorldToGrid(transform.position);
        }
        rigidbody2d.position = GridToWorld(playerGridPosition);
    }

    void OnDestroy()
    {
        TurnManager.Instance?.UnregisterPlayer();
    }

    private void OnEnable() => CombatEvents.PlayerDefeated += OnDefeat;
    private void OnDisable() => CombatEvents.PlayerDefeated -= OnDefeat;

    public void OnDefeat()
    {
        CombatEvents.RaiseDefeat(playerGridPosition, corpsePrefab);
        if (visual != null)
            visual.SetActive(false);
    }

    public void TakeTurn()
    {
        if (isDead)
            return;

        if (queuedDirection == Vector2Int.zero || IsMoving)
        {
            if (queuedDirection == Vector2Int.zero && !IsMoving)
                animator.SetBool(IsMovingHash, false);
            return;
        }

        Vector2Int targetPos = playerGridPosition + queuedDirection;
        IGridActor actor = QueryActorTile(targetPos, out bool isHardBlocked);

        if (isHardBlocked)
        {
            StartCoroutine(BumpMove(queuedDirection));
            return;
        }
        bool destinationWasCleared = actor == null;
        if (actor != null)
        {
            if (!actor.OnPlayerMoveInto(queuedDirection))
            {
                StartCoroutine(BumpMove(queuedDirection));
                return;
            }

            destinationWasCleared = actor is IPushable;
        }

        if (destinationWasCleared)
        {
            FinishTile finishTile = QueryFinishTile(targetPos);
            if (finishTile != null)
                finishTile.OnPlayerMoveInto(queuedDirection);
        }

        Vector3 from = GridToWorld(playerGridPosition);
        Vector3 to = GridToWorld(targetPos);
        UpdateAnimator(queuedDirection);
        playerGridPosition = targetPos;

        // A hole at the destination consumes the player once the move animation lands.
        if (HoleRegistry.TryGet(targetPos, out Hole hole))
            pendingHole = hole;

        // check for collectable hit
        Collider2D collectableHit = Physics2D.OverlapPoint(to, GridSettings.CollectableLayer);
        collectableHit?.GetComponent<Collectable>()?.OnPlayerMoveInto(queuedDirection);

        StartCoroutine(SmoothMove(from, to));
    }
    
    public void ClearInputs()
    {
        queuedDirection = Vector2Int.zero;
    }


    public void OnRestart(InputValue value)
    {    
        if (value.isPressed)
        LevelManager.Instance.ReplayLevel();
    }

    public void OnMove(InputValue value)
    {
        if (isDead)
            return;

        Vector2Int cardinal = SnapToCardinal(value.Get<Vector2>());
        // Only overwrite the buffer with a real direction — ignore zero (key-release)
        // events so they cannot wipe a buffered input mid-animation.
        //if (cardinal != Vector2Int.zero)
        //    queuedDirection = cardinal;
        queuedDirection = cardinal; // allow zero on release to clear the buffer
    }

    private static Vector2Int SnapToCardinal(Vector2 input)
    {
        if (input == Vector2.zero) return Vector2Int.zero;

        if (Mathf.Abs(input.x) >= Mathf.Abs(input.y))
            return input.x > 0f ? Vector2Int.right : Vector2Int.left;
        else
            return input.y > 0f ? Vector2Int.up : Vector2Int.down;
    }

    private IEnumerator BumpMove(Vector2Int direction)
    {
        IsMoving = true;
        UpdateAnimator(direction);
        yield return ActorUtils.BumpCoroutine(rigidbody2d, playerGridPosition, direction, moveDuration);
        if (queuedDirection == Vector2Int.zero) animator.SetBool(IsMovingHash, false);
        IsMoving = false;
        OnMovementComplete?.Invoke();
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
        if (queuedDirection == Vector2Int.zero) animator.SetBool(IsMovingHash, false);
        IsMoving = false;

        if (pendingHole != null)
        {
            // ensure player cannot move again, as it will cause null-ref on Animator...
            isDead = true;
            queuedDirection = Vector2Int.zero;

            Hole hole = pendingHole;
            pendingHole = null;
            hole.Consume(gameObject, isPlayer: true);
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
