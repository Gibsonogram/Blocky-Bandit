using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;

public class TurnManager : MonoBehaviour
{
    [SerializeField] private float moveRepeatDelay = 0.25f;

    private Coroutine repeatCoroutine;
    private bool directionChangedDuringMove;
    public static TurnManager Instance { get; private set; }
    public Vector2Int playerGridPosition => player.playerGridPosition;

    private PlayerController player;
    private readonly List<ITurnActor> turnActors = new();
    private PlayerInput playerInput;

    public IReadOnlyList<ITurnActor> Actors => turnActors;

    public void ClearActors() => turnActors.Clear();

    public void RegisterPlayer(PlayerController p)
    {
        player = p;
        player.OnMovementComplete += OnPlayerMovementComplete;
    }

    public void UnregisterPlayer()
    {
        // player could be destroyed by some other process...
        if (player == null) return;
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

        if (GameStateManager.Instance.CurrentState == GameState.PlayMode)
            StartCoroutine(RefreshVisionAfterStart());
    }

    private void ResolveActorCollisions()
    {
        var byTile = new Dictionary<Vector2Int, List<ITurnActor>>();
        foreach (var actor in turnActors)
        {
            if (!byTile.TryGetValue(actor.GridPosition, out var list))
            {
                list = new List<ITurnActor>();
                byTile[actor.GridPosition]= list;
            }
            list.Add(actor);
        } 

        foreach (var pair in byTile)
        {
            var list = pair.Value;
            if (list.Count < 2) continue;

            list.Sort((a,b) => b.CombatPriority.CompareTo(a.CombatPriority));
            for (int i=1; i < list.Count; i++)
            {
                list[i].OnDefeat();
            }
        }
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

    public void OnRestart(InputValue value)
    {    
        if (GameStateManager.Instance.CurrentState != GameState.PlayMode) return;
        if (value.isPressed)
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
        foreach (var actor in turnActors)
        {
            actor.TakeTurn();
        }

        ResolveActorCollisions();
        VisionOverlayRenderer.Instance?.Refresh();

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

    IEnumerator RepeatMove()
    {
        yield return new WaitForSeconds(moveRepeatDelay);
        player?.TakeTurn();
    }

}
