using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages the turn order for a turn-based game.
/// The player's input triggers a new turn; the player acts first,
/// then all registered enemy actors take their turns in order.
/// </summary>
public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }

    [SerializeField] private PlayerController player;

    // Register enemy/NPC actors here as the game grows.
    // private readonly List<IActorTurn> _actors = new();

    private bool _isTurnProcessing;

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
        // as soon as the current movement animation completes.
        player.OnMovementComplete += ProcessTurn;
    }

    private void OnDestroy()
    {
        player.OnMovementComplete -= ProcessTurn;
    }

    /// <summary>
    /// Called by the PlayerInput component when the Move action fires.
    /// Always forwards input to the player as a buffer.
    /// Only triggers a turn immediately if the player is not currently animating —
    /// if they are, the buffer will be consumed when OnMovementComplete fires.
    /// </summary>
    public void OnMove(InputValue value)
    {
        if (_isTurnProcessing) return;

        player.OnMove(value);

        if (!player.IsMoving)
            ProcessTurn();
    }
    
    private void ProcessTurn()
    {
        _isTurnProcessing = true;

        player.TakeTurn();

        // TODO: Iterate enemy/NPC actors here.
        // foreach (var actor in _actors) actor.TakeTurn();

        _isTurnProcessing = false;
    }

    /// <summary>Registers an actor to participate in the turn order.</summary>
    // public void RegisterActor(IActorTurn actor) => _actors.Add(actor);

    /// <summary>Removes an actor from the turn order.</summary>
    // public void UnregisterActor(IActorTurn actor) => _actors.Remove(actor);
}
