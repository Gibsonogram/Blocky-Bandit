using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }
    public Vector2Int playerGridPosition => player.playerGridPosition;

    private PlayerController player;
    private readonly List<ITurnActor> turnActors = new();
    private PlayerInput playerInput;

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
    }

    private void OnDestroy()
    {
        GameStateManager.Instance.OnStateChanged -= OnStateChanged;
    }


    public void RegisterActor(ITurnActor actor) => turnActors.Add(actor);
    public void UnregisterActor(ITurnActor actor) => turnActors.Remove(actor);

    public void OnRestart(InputValue value)
    {    
        if (GameStateManager.Instance.CurrentState != GameState.PlayMode) return;
        if (value.isPressed)
        LevelManager.Instance.ReplayLevel();
    }

    void OnStateChanged(GameState newState)
    {
        player?.ClearInputs();
        string actionMap = newState == GameState.PlayMode ? "Player" : "UI";
        playerInput.SwitchCurrentActionMap(actionMap);
    }

    public void OnMove(InputValue value)
    {
        if (GameStateManager.Instance.CurrentState != GameState.PlayMode) return;
        if (player == null) return;
        player.OnMove(value);
        if (!player.IsMoving) player.TakeTurn();
    }
    
    private void OnPlayerMovementComplete()
    {
        foreach (var actor in turnActors)
        {
            actor.TakeTurn();
        }
        VisionOverlayRenderer.Instance?.Refresh();
    }

}
