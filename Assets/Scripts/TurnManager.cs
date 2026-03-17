using UnityEngine;
using UnityEngine.InputSystem;

public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }

    [SerializeField] private PlayerController player;
    private bool isTurnProcessing;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Subscribe to the buffered-input event so that a turn fires automatically
        player.OnMovementComplete += ProcessTurn;
        GameStateManager.Instance.OnStateChanged += OnStateChanged;
    }

    private void OnDestroy()
    {
        player.OnMovementComplete -= ProcessTurn;
        GameStateManager.Instance.OnStateChanged -= OnStateChanged;
    }

    void OnStateChanged(GameState newState)
    {
        Debug.Log($"Game state changed to {GameStateManager.Instance.CurrentState}");
        // stuff turn manager needs to do when switched to finish state... 
        // potentially remove all queued turns, reset some things...
    }

    public void OnMove(InputValue value)
    {
        if (GameStateManager.Instance.CurrentState != GameState.PlayMode) return;
        
        if (isTurnProcessing) return;

        player.OnMove(value);
        if (!player.IsMoving) ProcessTurn();
    }
    
    private void ProcessTurn()
    {
        isTurnProcessing = true;
        player.TakeTurn();
        isTurnProcessing = false;
    }

}
