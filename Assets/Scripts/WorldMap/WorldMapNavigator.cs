using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;

public class WorldMapNavigator : MonoBehaviour
{
    private const float MinimumInputMagnitudeSquared = 0.01f;
    private const float MinimumNodeOffsetSquared = 0.0001f;

    [SerializeField] private WorldMapNode[] nodes;
    [SerializeField] private Transform playerSprite;
    [Tooltip("Seconds the player takes to travel between two nodes.")]
    [SerializeField] private float lerpDuration = 0.6f;
    // The same feel as the gameplay bump (a quarter of the actor's own size, out and back),
    // scaled for the world map player, who is two tiles tall.
    [SerializeField] private float bumpDistance = 0.5f;
    [SerializeField] private float bumpDuration = 0.3f;
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

        RegisterUnlistedNodes();
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
        if (input.sqrMagnitude < MinimumInputMagnitudeSquared) return;
        TryNavigate(input.normalized);
    }

    // The node list is the path: every node is linked to its two neighbours and the last node loops back
    // to the first. Rather than following the list blindly, we walk to the neighbour that actually sits in
    // the direction the player pressed, so the loop back to the start (node6 -> node1) still moves the
    // player the way they pushed the stick instead of dragging them against it.
    private void TryNavigate(Vector2 direction)
    {
        if (nodes == null || nodes.Length < 2) return;
        if (!TryGetNode(currentNodeIndex, out WorldMapNode departingNode)) return;
        if (playerSprite == null)
        {
            Debug.LogWarning("World Map Navigator has no player sprite assigned.", this);
            return;
        }

        int targetIndex = FindNeighbourInDirection(departingNode, direction);
        if (targetIndex == currentNodeIndex) return;
        if (!TryGetNode(targetIndex, out WorldMapNode destinationNode)) return;

        // Locked worlds are walled off: the player can reach the first locked node (so the next
        // world reads as a goal) but pushing past it bumps them back instead of travelling.
        if (!IsNodeReachable(targetIndex))
        {
            PlayBump(destinationNode);
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

    private int FindNeighbourInDirection(WorldMapNode from, Vector2 direction)
    {
        int forwardIndex = WrapIndex(currentNodeIndex + 1);
        int backwardIndex = WrapIndex(currentNodeIndex - 1);

        bool forwardIsAhead = IsNodeInDirection(from, forwardIndex, direction);
        bool backwardIsAhead = IsNodeInDirection(from, backwardIndex, direction);

        // Both neighbours sit ahead of the player (the path doubles back, as it does at the top of the
        // map): keep progressing along the path with the loop wrapping from the last node to the first.
        if (forwardIsAhead && backwardIsAhead) return forwardIndex;
        if (forwardIsAhead) return forwardIndex;
        if (backwardIsAhead) return backwardIndex;

        // Nothing lies in that direction, so leave the player where they are.
        return currentNodeIndex;
    }

    private bool IsNodeInDirection(WorldMapNode from, int toIndex, Vector2 direction)
    {
        if (!TryGetNode(toIndex, out WorldMapNode to) || to == null) return false;

        Vector2 offset = to.transform.position - from.transform.position;
        if (offset.sqrMagnitude <= MinimumNodeOffsetSquared) return false;

        return Vector2.Dot(offset.normalized, direction) > 0f;
    }

    private int WrapIndex(int index)
    {
        int count = nodes.Length;
        return ((index % count) + count) % count;
    }

    // The path is a loop, so the unlocked worlds form one stretch of it with a locked neighbour on
    // each side. Those two first-locked nodes are what the player is collecting towards, so they
    // stay visitable; everything further along the loop is walled off.
    private bool IsNodeReachable(int index)
    {
        if (IsNodeUnlocked(index)) return true;

        // A save with nothing unlocked would otherwise trap the player on the map entirely.
        if (!HasUnlockedNode()) return true;

        return IsNodeUnlocked(WrapIndex(index + 1)) || IsNodeUnlocked(WrapIndex(index - 1));
    }

    private bool HasUnlockedNode()
    {
        for (int i = 0; i < nodes.Length; i++)
        {
            if (IsNodeUnlocked(i)) return true;
        }

        return false;
    }

    private bool IsNodeUnlocked(int index)
    {
        return TryGetNode(index, out WorldMapNode node) && node != null && IsWorldUnlocked(node.WorldData);
    }

    // ActorUtils.BumpCoroutine in miniature: nudge towards the node the player pushed into, then
    // settle back on the current node. The travel index never changes, so nothing is consumed.
    private void PlayBump(WorldMapNode blockedNode)
    {
        Vector3 from = playerSprite.position;
        Vector3 offset = blockedNode.transform.position - from;
        if (offset.sqrMagnitude <= MinimumNodeOffsetSquared) return;

        float halfDuration = Mathf.Max(0f, bumpDuration) * 0.5f;
        Vector3 bumpTarget = from + offset.normalized * Mathf.Max(0f, bumpDistance);

        isMoving = true;
        playerSprite.DOKill();
        playerSprite.DOMove(bumpTarget, halfDuration)
            .SetUpdate(false)
            .SetEase(Ease.OutQuad)
            .OnComplete(() => playerSprite.DOMove(from, halfDuration)
                .SetUpdate(false)
                .SetEase(Ease.InQuad)
                .OnComplete(() => isMoving = false));
    }

    // The node list controls the path order, so nodes dropped into the scene but left out of the list are
    // appended to the end of the loop and reported, instead of silently becoming unreachable.
    private void RegisterUnlistedNodes()
    {
        WorldMapNode[] sceneNodes = FindObjectsByType<WorldMapNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (sceneNodes.Length == 0) return;

        List<WorldMapNode> path = nodes != null ? new List<WorldMapNode>(nodes) : new List<WorldMapNode>();

        foreach (WorldMapNode sceneNode in sceneNodes)
        {
            if (sceneNode == null || path.Contains(sceneNode)) continue;

            path.Add(sceneNode);
            Debug.LogWarning(
                $"World map node '{sceneNode.name}' is missing from the World Map Navigator node list; it was appended to the end of the loop. Add it to the list to choose where it sits in the path.",
                this);
        }

        nodes = path.ToArray();
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
