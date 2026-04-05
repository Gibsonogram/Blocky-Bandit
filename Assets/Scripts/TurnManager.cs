using UnityEngine;
using UnityEngine.InputSystem;

public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }

    [SerializeField] private PlayerController player;
    private PlayerInput playerInput;
    private bool isTurnProcessing;

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
        player.ClearInputs();
        playerInput.SwitchCurrentActionMap(newState == GameState.EndScreen ? "UI" : "Player");
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
