using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }
    public Vector2Int playerGridPosition => player.playerGridPosition;

    [SerializeField] private PlayerController player;

    private readonly List<ITurnActor> turnActors = new();
    private PlayerInput playerInput;

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
        // Subscribe to the buffered-input event so that a turn fires automatically
        player.OnMovementComplete += OnPlayerMovementComplete;
        GameStateManager.Instance.OnStateChanged += OnStateChanged;
    }

    private void OnDestroy()
    {
        player.OnMovementComplete -= OnPlayerMovementComplete;
        GameStateManager.Instance.OnStateChanged -= OnStateChanged;
    }



    public void RegisterActor(ITurnActor actor) => turnActors.Add(actor);
    public void UnregisterActor(ITurnActor actor) => turnActors.Remove(actor);



    void OnStateChanged(GameState newState)
    {
        Debug.Log($"Game state changed to {GameStateManager.Instance.CurrentState}");
        player.ClearInputs();
        playerInput.SwitchCurrentActionMap(newState == GameState.EndScreen ? "UI" : "Player");
    }

    public void OnMove(InputValue value)
    {
        if (GameStateManager.Instance.CurrentState != GameState.PlayMode) return;
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
