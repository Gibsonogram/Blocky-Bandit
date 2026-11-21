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

    [Header("Movement Variants")]
    [SerializeField] private AnimationClip moveLeftAltClip;
    [SerializeField] private AnimationClip moveRightAltClip;
    
    public Vector2Int playerGridPosition;
    public event Action OnMovementComplete;
    
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private const string MoveLeftClipName = "MoveLeftClip";
    private const string MoveRightClipName = "MoveRightClip";
    private Rigidbody2D rigidbody2d;
    private Animator animator;
    private Vector2Int queuedDirection;
    private Hole pendingHole;
    private bool isDead;
    private AnimatorOverrideController overrideController;
    private AnimationClip moveLeftBaseClip;
    private AnimationClip moveRightBaseClip;
    private bool leftUsingAlt;
    private bool rightUsingAlt;
    public bool IsMoving { get; private set; }

    // True when the action that just finished was a bump: the player was blocked and never
    // left its tile, so it is not a simulation step. TurnManager reads this to leave the
    // whole board (actor turns, mine fuses, vision cones) exactly as it was.
    public bool LastMoveWasBump { get; private set; }

    private void Awake()
    {
        rigidbody2d = GetComponent<Rigidbody2D>();
        animator = GetComponentInChildren<Animator>();
        SetUpMoveVariants();
    }

    // Wraps the shared animator controller so the left/right walking clips can be swapped at
    // runtime without editing the controller asset. Does nothing when no alternate clips are
    // assigned, so the animator keeps working untouched if these fields are left empty.
    private void SetUpMoveVariants()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (moveLeftAltClip == null && moveRightAltClip == null) return;

        overrideController = new AnimatorOverrideController(animator.runtimeAnimatorController);
        animator.runtimeAnimatorController = overrideController;

        var overrides = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>();
        overrideController.GetOverrides(overrides);
        foreach (var pair in overrides)
        {
            if (pair.Key == null) continue;
            if (pair.Key.name == MoveLeftClipName) moveLeftBaseClip = pair.Key;
            else if (pair.Key.name == MoveRightClipName) moveRightBaseClip = pair.Key;
        }
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
        UpdateAnimator(queuedDirection, true);
        playerGridPosition = targetPos;

        // A hole at the destination consumes the player once the move animation lands.
        // Locking the outcome now (not just once the fall animation finishes) is what
        // makes the anti-escape guard in TurnManager.OnMove take effect immediately.
        if (HoleRegistry.TryGet(targetPos, out Hole hole))
        {
            pendingHole = hole;
            PauseUI.TryLockGameOver();
        }

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
        UpdateAnimator(direction, false);
        yield return ActorUtils.BumpCoroutine(rigidbody2d, playerGridPosition, direction, moveDuration);
        if (queuedDirection == Vector2Int.zero) animator.SetBool(IsMovingHash, false);
        IsMoving = false;
        // Flagged before the event so TurnManager sees it as a bump and freezes the board.
        LastMoveWasBump = true;
        OnMovementComplete?.Invoke();
    }

    private IEnumerator SmoothMove(Vector3 from, Vector3 to)
    {
        ActorSorting.BeginStep(gameObject, from, to);
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
        ActorSorting.EndStep(gameObject);
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
        else if (PauseUI.IsGameOverAwaitingPresentation)
        {
            // Player walked into a Rook/Bishop this move — the walk-in animation just
            // landed, so it's safe to present the death now.
            isDead = true;
            queuedDirection = Vector2Int.zero;
            PauseUI.ShowGameOver();
        }

        // Any completed non-bump move is a real step, including one that ends in a hole.
        LastMoveWasBump = false;
        OnMovementComplete?.Invoke();
    }

    private void UpdateAnimator(Vector2Int direction, bool advanceVariant)
    {
        if (animator == null) return;

        if (advanceVariant)
            AdvanceVariant(direction);

        animator.SetFloat(MoveXHash, direction.x);
        animator.SetFloat(MoveYHash, direction.y);
        animator.SetBool(IsMovingHash, true);
    }

    // Swaps which walking clip the stepped direction plays. The variant selected now is the one
    // that plays, and the stored flag is flipped afterwards, so the first step in a direction
    // always plays the original clip and each following step alternates between the two loops.
    private void AdvanceVariant(Vector2Int direction)
    {
        if (overrideController == null) return;

        if (direction.x < 0 && moveLeftBaseClip != null && moveLeftAltClip != null)
        {
            overrideController[moveLeftBaseClip] = leftUsingAlt ? moveLeftAltClip : moveLeftBaseClip;
            leftUsingAlt = !leftUsingAlt;
        }
        else if (direction.x > 0 && moveRightBaseClip != null && moveRightAltClip != null)
        {
            overrideController[moveRightBaseClip] = rightUsingAlt ? moveRightAltClip : moveRightBaseClip;
            rightUsingAlt = !rightUsingAlt;
        }
    }

}
