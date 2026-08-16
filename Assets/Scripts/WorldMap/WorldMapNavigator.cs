using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;

public class WorldMapNavigator : MonoBehaviour
{
    [SerializeField] private WorldMapNode[] nodes;
    [SerializeField] private Transform playerSprite;
    [SerializeField] private float lerpDuration = 0.3f;
    // Assign the WorldMapActions.inputactions asset here in the Inspector
    [SerializeField] private InputActionAsset inputActionAsset;

    private InputActionMap worldMapActionMap;
    private InputAction navigateAction;
    private InputAction acceptAction;
    private InputAction cancelAction;
    private int currentNodeIndex;
    private bool isMoving;

    private void Awake()
    {
        worldMapActionMap = inputActionAsset.FindActionMap("WorldMap", throwIfNotFound: true);
        navigateAction = worldMapActionMap.FindAction("Navigate", throwIfNotFound: true);
        acceptAction = worldMapActionMap.FindAction("Accept", throwIfNotFound: true);
        cancelAction = worldMapActionMap.FindAction("Cancel", throwIfNotFound: true);
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


    }

    private void Start()
    {
        if (TryGetNode(currentNodeIndex, out WorldMapNode currentNode) && playerSprite != null)
            playerSprite.position = currentNode.transform.position;

        RefreshNodeVisuals();
        SelectCurrentNode();
    }

    private void OnStateChanged(GameState state)
    {
        if (state == GameState.WorldMap)
        {
            worldMapActionMap.Enable();
            RefreshNodeVisuals();
            SelectCurrentNode();
        }
        else
        {
            worldMapActionMap.Disable();
        }
    }

    private void OnNavigate(InputAction.CallbackContext context)
    {
        if (isMoving) return;

        Vector2 input = context.ReadValue<Vector2>();
        if (input.y == 0f) return;
        TryNavigate(input.y > 0f ? 1 : -1);
    }

    private void TryNavigate(int direction)
    {
        int targetIndex = currentNodeIndex + direction;
        if (targetIndex < 0 || targetIndex >= nodes.Length || targetIndex == currentNodeIndex) return;
        if (!TryGetNode(currentNodeIndex, out WorldMapNode departingNode)) return;
        if (!TryGetNode(targetIndex, out WorldMapNode destinationNode)) return;
        if (playerSprite == null)
        {
            Debug.LogWarning("World Map Navigator has no player sprite assigned.", this);
            return;
        }

        departingNode.SetSelected(false);
        isMoving = true;
        playerSprite.DOMove(destinationNode.transform.position, lerpDuration)
            .SetUpdate(false)
            .OnComplete(() =>
            {
                currentNodeIndex = targetIndex;
                isMoving = false;
                destinationNode.RefreshPresentation();
                destinationNode.SetSelected(true);
            });
    }

    private void OnAccept(InputAction.CallbackContext context)
    {
        if (isMoving || !TryGetNode(currentNodeIndex, out WorldMapNode node)) return;
        if (node.WorldData == null || !IsWorldUnlocked(node.WorldData)) return;

        GameStateManager.Instance.ChangeState(GameState.Menus);
        LevelManager.Instance.SelectWorld(node.WorldData);
    }

    private void OnCancel(InputAction.CallbackContext context) => LevelManager.Instance.LoadMainMenu();

    private void RefreshNodeVisuals()
    {
        if (nodes == null) return;

        foreach (WorldMapNode node in nodes)
        {
            if (node == null)
            {
                Debug.LogWarning("World Map Navigator contains a missing node reference.", this);
                continue;
            }

            node.RefreshPresentation();
        }
    }

    private void SelectCurrentNode()
    {
        if (TryGetNode(currentNodeIndex, out WorldMapNode currentNode))
            currentNode.SetSelected(true);
    }

    private bool TryGetNode(int index, out WorldMapNode node)
    {
        node = null;
        if (nodes == null || index < 0 || index >= nodes.Length)
        {
            Debug.LogWarning("World Map Navigator has no node at the requested index.", this);
            return false;
        }

        node = nodes[index];
        if (node != null) return true;

        Debug.LogWarning($"World Map Navigator has a missing node reference at index {index}.", this);
        return false;
    }

    private bool IsWorldUnlocked(WorldData world)
    {
        return world != null && SaveManager.Instance != null &&
               world.collectableUnlockThreshold >= 0 &&
               SaveManager.Instance.GetTotalCollectables() >= world.collectableUnlockThreshold;
    }
}
