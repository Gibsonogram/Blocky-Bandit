using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening;

public class WorldMapNavigator : MonoBehaviour
{
    [SerializeField] private WorldMapNode[] nodes;
    [SerializeField] private Transform playerSprite;
    [SerializeField] private float lerpDuration = 0.3f;
    // Assign the WorldMapActions.inputactions asset here in the Inspector
    [SerializeField] private InputActionAsset inputActionAsset;

    private InputAction navigateAction;
    private InputAction acceptAction;
    private InputAction cancelAction;

    private int currentNodeIndex;
    private bool isMoving;

    private void Awake()
    {
        InputActionMap worldMap = inputActionAsset.FindActionMap("WorldMap", throwIfNotFound: true);
        navigateAction = worldMap.FindAction("Navigate", throwIfNotFound: true);
        acceptAction = worldMap.FindAction("Accept", throwIfNotFound: true);
        cancelAction = worldMap.FindAction("Cancel", throwIfNotFound: true);
    }

    private void OnEnable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged += OnStateChanged;
            OnStateChanged(GameStateManager.Instance.CurrentState);
        }

        navigateAction.performed += OnNavigate;
        acceptAction.performed += OnAccept;
        cancelAction.performed += OnCancel;
    }

    private void OnDisable()
    {
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= OnStateChanged;

        if (navigateAction != null) navigateAction.performed -= OnNavigate;
        if (acceptAction != null) acceptAction.performed -= OnAccept;
        if (cancelAction != null) cancelAction.performed -= OnCancel;

        inputActionAsset?.Disable();
    }

    private void Start()
    {
        if (nodes.Length > 0)
            playerSprite.position = nodes[currentNodeIndex].transform.position;

        RefreshNodeVisuals();
    }

    private void OnStateChanged(GameState state)
    {
        if (state == GameState.WorldMap)
            inputActionAsset.Enable();
        else
            inputActionAsset.Disable();
    }

    private void OnNavigate(InputAction.CallbackContext context)
    {
        if (isMoving) return;

        Vector2 input = context.ReadValue<Vector2>();
        if (input.y == 0f) return; 
        TryNavigate(input.y > 0f ? 1 : -1);
    }

    private void TryNavigate(int dir)
    {
        int target = currentNodeIndex + dir;
        if (target < 0 || target >= nodes.Length) return;

        currentNodeIndex = target;
        isMoving = true;
        playerSprite.DOMove(nodes[currentNodeIndex].transform.position, lerpDuration)
            .SetUpdate(false)
            .OnComplete(() => isMoving = false);
    }

    private void OnAccept(InputAction.CallbackContext context)
    {
        
        if (isMoving) return;
        WorldMapNode node = nodes[currentNodeIndex];
        if (!IsWorldUnlocked(node.WorldData)) return;

        enabled = false;
        inputActionAsset.Disable();

        GameStateManager.Instance.ChangeState(GameState.Menus);
        LevelManager.Instance.SelectWorld(node.WorldIndex);
    }

    private void OnCancel(InputAction.CallbackContext context) => LevelManager.Instance.LoadMainMenu();

    private void RefreshNodeVisuals()
    {
        foreach (WorldMapNode node in nodes)
            node.SetLocked(!IsWorldUnlocked(node.WorldData));
    }

    private bool IsWorldUnlocked(WorldData world)
    {
        return SaveManager.Instance.GetTotalCollectables() >= world.collectableUnlockThreshold;
    }
}
