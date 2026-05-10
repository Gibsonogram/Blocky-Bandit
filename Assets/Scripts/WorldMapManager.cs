using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Manages the world map screen. Nodes self-register via RegisterNode on Start.
// Drives EventSystem selection and cursor placement when WorldMap state is entered.
public class WorldMapManager : MonoBehaviour
{
    public static WorldMapManager Instance { get; private set; }

    [SerializeField] private WorldLevelSelectUI levelSelectUI;
    [SerializeField] private WorldMapCursor cursor;
    [SerializeField] private CinemachineCamera cinemachineCamera;
    [SerializeField] private WorldNodePreviewPanel previewPanel;

    private readonly List<WorldMapNode> nodes = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        GameStateManager.Instance.OnStateChanged += OnStateChanged;
        levelSelectUI.Hide();
        previewPanel.Hide();
    }

    private void OnDestroy()
    {
        GameStateManager.Instance.OnStateChanged -= OnStateChanged;
    }

    public void RegisterNode(WorldMapNode node)
    {
        if (!nodes.Contains(node))
            nodes.Add(node);
    }

    public void ShowPreview(WorldMapNode node)
    {
        previewPanel.Refresh(node.WorldData, node.WorldIndex);
    }

    private void OnStateChanged(GameState newState)
    {
        if (newState == GameState.WorldMap)
        {
            levelSelectUI.Hide();
            RefreshNodes();
            StartCoroutine(SelectStartNodeNextFrame());
        }
        else if (newState == GameState.WorldLevelSelect)
        {
            previewPanel.Hide();
            levelSelectUI.Show(LevelManager.Instance.CurrentWorld, LevelManager.Instance.CurrentWorldIndex);
            EventSystem.current.SetSelectedGameObject(null);
        }
        else
        {
            previewPanel.Hide();
            levelSelectUI.Hide();
        }
    }

    private IEnumerator SelectStartNodeNextFrame()
    {
        yield return null;
        SelectStartNode();
    }

    private void RefreshNodes()
    {
        foreach (WorldMapNode node in nodes)
            node.SetUnlocked(node.IsUnlocked());
    }

    private void SelectStartNode()
    {
        WorldMapNode startNode = nodes.Count > 0 ? nodes[0] : null;
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            if (nodes[i].IsUnlocked())
            {
                startNode = nodes[i];
                break;
            }
        }

        if (startNode == null) return;

        if (cinemachineCamera != null)
            cinemachineCamera.Follow = startNode.transform;

        cursor.SnapToNode(startNode);
        ShowPreview(startNode);
        EventSystem.current.SetSelectedGameObject(startNode.GetComponent<Button>().gameObject);
    }
}
