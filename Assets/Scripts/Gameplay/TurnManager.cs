using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;

// TurnManager is for base PLAYER ActionMap, it does not control the WORLDMAP ActionMap
public class TurnManager : MonoBehaviour
{
    [SerializeField] private float moveRepeatDelay = 0.25f;

    private Coroutine repeatCoroutine;
    private bool directionChangedDuringMove;
    public static TurnManager Instance { get; private set; }

    // Last tile read from the player while it still existed. Reads can outlive the player
    // object: a hole consumes it, and advancing the level then fires a PlayMode state change
    // while the outgoing level's actors are still alive, so their cone refresh asks for the
    // player position after the component is gone. Falling back to the cached tile keeps that
    // refresh from throwing (the overlay is cleared for the new scene anyway).
    private Vector2Int lastKnownPlayerPosition;
    public Vector2Int playerGridPosition
    {
        get
        {
            if (player != null)
                lastKnownPlayerPosition = player.playerGridPosition;
            return lastKnownPlayerPosition;
        }
    }

    private PlayerController player;
    private readonly List<ITurnActor> turnActors = new();
    // Scratch buffer for the per-turn priority ordering; reused so a turn allocates nothing.
    private readonly List<ITurnActor> orderedActors = new();
    // Reused by ResolveActorCollisions: the per-tile buckets, the ones in use this turn, and
    // the pool of emptied ones waiting to be rented again.
    private readonly Dictionary<Vector2Int, List<ITurnActor>> collisionBuckets = new();
    private readonly List<List<ITurnActor>> usedBuckets = new();
    private readonly List<List<ITurnActor>> freeBuckets = new();
    // Reused by NotifyExplosionReactors, so a blast does not copy the actor list.
    private readonly List<ITurnActor> reactorSnapshot = new();
    private PlayerInput playerInput;
    private bool playerReachedFinish;

    public IReadOnlyList<ITurnActor> Actors => turnActors;

    public void ClearActors()
    {
        turnActors.Clear();
        playerReachedFinish = false;
    }

    // Flagged by FinishTile when the player steps onto the exit. The win is resolved at
    // the end of the turn (ResolvePlayerReachedFinish) so enemies get their turn first.
    public void FlagPlayerReachedFinish() => playerReachedFinish = true;

    public void RegisterPlayer(PlayerController p)
    {
        player = p;
        lastKnownPlayerPosition = p.playerGridPosition;
        player.OnMovementComplete += OnPlayerMovementComplete;
    }

    public void UnregisterPlayer()
    {
        // player could be destroyed by some other process...
        if (player == null) return;
        // Keep the tile it died on: the cone refresh that follows a hole death still asks for
        // the player position, and the getter must not touch the destroyed component.
        lastKnownPlayerPosition = player.playerGridPosition;
        player.OnMovementComplete -= OnPlayerMovementComplete;
        player = null;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        playerInput = GetComponent<PlayerInput>();
    }

    private void Start()
    {
        GameStateManager.Instance.OnStateChanged += OnStateChanged;
        SceneManager.sceneLoaded += OnSceneLoaded;

        OnStateChanged(GameStateManager.Instance.CurrentState);

        if (GameStateManager.Instance.CurrentState == GameState.PlayMode)
            StartCoroutine(RefreshVisionAfterStart());
    }

    private void ResolveActorCollisions()
    {
        // Recycle last turn's per-tile buckets rather than allocating a dictionary, a list per
        // tile and their backing arrays on every completed turn. The dictionary is cleared
        // first, so buckets are only ever handed out through the pool.
        for (int i = 0; i < usedBuckets.Count; i++)
        {
            usedBuckets[i].Clear();
            freeBuckets.Add(usedBuckets[i]);
        }
        usedBuckets.Clear();
        collisionBuckets.Clear();

        foreach (var actor in turnActors)
        {
            if (!collisionBuckets.TryGetValue(actor.GridPosition, out var list))
            {
                list = RentBucket();
                collisionBuckets[actor.GridPosition] = list;
            }
            list.Add(actor);
        }

        // Same enumeration, same insertion order as the fresh dictionary this replaced, so the
        // order overlapping actors are defeated in is unchanged.
        foreach (var pair in collisionBuckets)
        {
            var list = pair.Value;
            if (list.Count < 2) continue;

            list.Sort((a,b) => b.CombatPriority.CompareTo(a.CombatPriority));
            for (int i=1; i < list.Count; i++)
            {
                ITurnActor loser = list[i];

                // Fixed scenery survives any overlap: it has no defeat transition, and
                // unregistering its cone here would erase a hazard that is still standing.
                if (loser is IIndestructible)
                    continue;

                // Clear the loser's vision cone here rather than relying on its OnDestroy:
                // Destroy() is deferred to the end of the frame, so OnDestroy hasn't run
                // yet when this turn's Refresh() collects tiles below — the dead actor's
                // cone would otherwise linger until the following turn.
                if (loser is IVisionSource visionSource)
                    VisionOverlayRenderer.Instance?.UnregisterSource(visionSource);

                loser.OnDefeat();
            }
        }
    }

    // Hands out an emptied bucket from the pool, growing it only on the first couple of turns.
    private List<ITurnActor> RentBucket()
    {
        List<ITurnActor> bucket;
        if (freeBuckets.Count > 0)
        {
            int last = freeBuckets.Count - 1;
            bucket = freeBuckets[last];
            freeBuckets.RemoveAt(last);
        }
        else
        {
            bucket = new List<ITurnActor>();
        }

        usedBuckets.Add(bucket);
        return bucket;
    }

    private void OnDestroy()
    {
        GameStateManager.Instance.OnStateChanged -= OnStateChanged;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (GameStateManager.Instance.CurrentState == GameState.PlayMode)
            StartCoroutine(RefreshVisionAfterStart());
    }

    private IEnumerator RefreshVisionAfterStart()
    {
        yield return null; // let all actor Start() calls complete and register their sources
        VisionOverlayRenderer.Instance?.Refresh();
    }

    public void OnBack(InputValue value)
    {
        if (!value.isPressed) return;
        UINavigator.Instance.OnBack();
    }
    public void RegisterActor(ITurnActor actor) => turnActors.Add(actor);
    public void UnregisterActor(ITurnActor actor) => turnActors.Remove(actor);

    // Restart is bound in both action maps: "Player" while playing, and "UI" so the same
    // key/button still reaches this handler while the pause screen owns input. ReplayLevel
    // clears the navigator stack, resets the outcome lock and reloads the level, so the
    // pause panel is dismissed as part of the same restart it always was in play mode.
    public void OnRestart(InputValue value)
    {
        if (!value.isPressed) return;

        GameState state = GameStateManager.Instance.CurrentState;
        if (state != GameState.PlayMode && state != GameState.PauseScreen) return;

        LevelManager.Instance.ReplayLevel();
    }

    public void OnPause(InputValue value)
    {
        if (GameStateManager.Instance.CurrentState != GameState.PlayMode) return;
        if (!value.isPressed) return;
        PauseUI.Trigger(PauseContext.ManualPause);
    }

    void OnStateChanged(GameState newState)
    {
        player?.ClearInputs();
        string actionMap = newState == GameState.PlayMode ? "Player" : "UI";
        playerInput.SwitchCurrentActionMap(actionMap);

        if (newState != GameState.PlayMode)
            VisionOverlayRenderer.Instance?.Clear();
    }

    public void OnMove(InputValue value)
    {
        if (GameStateManager.Instance.CurrentState != GameState.PlayMode) return;
        // Freeze input the instant a kill is locked in, even though its presentation
        // (hide player, pause screen) is still waiting on the killing move's animation.
        // Otherwise the player could dodge away during that window.
        if (PauseUI.OutcomeResolved) return;
        if (player == null) return;

        if (repeatCoroutine != null)
        {
            StopCoroutine(repeatCoroutine);
            repeatCoroutine = null;
        }

        if (player.IsMoving && value.Get<Vector2>() != Vector2.zero)
            directionChangedDuringMove = true;

        player.OnMove(value);
        if (!player.IsMoving) player.TakeTurn();
    }
    
    private void OnPlayerMovementComplete()
    {
        // Once the level is decided (win or loss), stop resolving turns so actors can't
        // take a post-outcome turn or flip a win into a loss.
        if (PauseUI.OutcomeResolved) return;

        // A bump is not a simulation step. The player was blocked and never left its tile,
        // so there is nothing for the board to react to: no actor takes a turn, no mine
        // fuse ticks and no vision cone changes. The input is simply offered again, so a
        // bump costs the player nothing instead of handing the enemies a free move.
        if (player != null && player.LastMoveWasBump)
        {
            FollowUpPlayerInput();
            return;
        }

        TakeActorTurns();

        ResolveActorCollisions();
        if (ResolvePendingExplosions())
            NotifyExplosionReactors();
        ResolvePlayerReachedFinish();
        VisionOverlayRenderer.Instance?.Refresh();

        FollowUpPlayerInput();
    }

    // Either consumes a direction the player switched to mid-move, or schedules the
    // hold-to-repeat retry. Shared by real moves and bumps: a bump still has to be followed
    // up, it just doesn't take a simulation step.
    private void FollowUpPlayerInput()
    {
        if (directionChangedDuringMove)
        {
            directionChangedDuringMove = false;
            player?.TakeTurn();
        }
        else
        {
            if (repeatCoroutine != null) StopCoroutine(repeatCoroutine);
            repeatCoroutine = StartCoroutine(RepeatMove());
        }
    }

    // Turn order is defined by CombatPriority, lowest first: bishops/queens, then rooks, then
    // cannons, then mines. Registration order follows the scene hierarchy, so relying on it
    // made actor behaviour depend on where a prefab happened to sit in each level — a mine
    // could take its turn before the rook or bishop it watches, evaluate the pre-move board,
    // and start its fuse one turn late. Mine.CombatPriority is the highest precisely so the
    // fuse always reads the board every other actor has already committed; a cannon sits just
    // below it so its shot lands on settled positions before the fuses tick.
    // Ordering is stable within a priority (registration order), so two actors of equal
    // priority always act in the same sequence and a turn is fully deterministic.
    private void TakeActorTurns()
    {
        orderedActors.Clear();

        int lowest = int.MaxValue;
        int highest = int.MinValue;
        foreach (var actor in turnActors)
        {
            int priority = actor.CombatPriority;
            if (priority < lowest) lowest = priority;
            if (priority > highest) highest = priority;
        }

        for (int priority = lowest; priority <= highest; priority++)
        {
            foreach (var actor in turnActors)
            {
                if (actor.CombatPriority == priority)
                    orderedActors.Add(actor);
            }
        }

        foreach (var actor in orderedActors)
            actor.TakeTurn();
    }


    // Resolves the win at the end of the turn, after enemies have moved and any catch or
    // mine blast has resolved. If the player was caught this turn, OutcomeResolved is
    // already set and the win is skipped, so death takes priority over reaching the exit.
    private void ResolvePlayerReachedFinish()
    {
        if (!playerReachedFinish)
            return;
        playerReachedFinish = false;

        if (PauseUI.OutcomeResolved)
            return;
        PauseUI.Trigger(PauseContext.LevelComplete);
    }

    // Mines flag themselves when their fuse hits zero during TakeTurn, but resolve their
    // blast here so it lands on final logical positions after every actor has moved. This
    // runs before the vision refresh so caught actors' cones clear the same turn.
    // Drains repeatedly: a blast can chain-detonate nearby mines, which flag themselves
    // during ResolveDetonation and are picked up on the next pass until none remain.
    // Returns true if any mine detonated this turn.
    private bool ResolvePendingExplosions()
    {
        bool exploded = false;
        while (true)
        {
            Mine pending = null;
            foreach (var actor in turnActors)
            {
                if (actor is Mine mine && mine != null && mine.IsPendingDetonation)
                {
                    pending = mine;
                    break;
                }
            }
            if (pending == null)
                break;

            pending.ResolveDetonation();
            exploded = true;
        }
        return exploded;
    }

    // After a blast, surviving actors re-sense the settled board so their state (line of
    // sight, chase target) is correct this same turn. State only; no additional movement.
    private void NotifyExplosionReactors()
    {
        // Snapshot first, because reacting can unregister/destroy actors while the live list
        // is walked. The buffer is reused: nothing re-enters this method during a blast, so
        // one list is enough and a detonation allocates nothing.
        reactorSnapshot.Clear();
        reactorSnapshot.AddRange(turnActors);

        foreach (var actor in reactorSnapshot)
        {
            if (actor is IExplosionReactor reactor && actor is Object obj && obj != null)
                reactor.ReactToExplosion();
        }
    }

    // Something that leaves the board after this turn's Refresh has already run asks for a
    // repaint instead of waiting for the next turn. A victim's move tween lands after the turn
    // loop finished (falling into a hole), and a mine's blast frees its tile late too; either
    // can be the body that was blocking a cone. Survivors re-sense the settled board - state
    // only, no extra movement - so an actor that can now see the player switches to chase, and
    // the overlay is repainted with the opened tiles this same turn.
    public void NotifyBoardChanged()
    {
        if (PauseUI.OutcomeResolved) return;

        NotifyExplosionReactors();
        VisionOverlayRenderer.Instance?.Refresh();
    }

    IEnumerator RepeatMove()
    {
        yield return new WaitForSeconds(moveRepeatDelay);
        if (PauseUI.OutcomeResolved) yield break;
        player?.TakeTurn();
    }

}
