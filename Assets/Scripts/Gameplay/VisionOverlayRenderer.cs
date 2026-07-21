using System.Collections.Generic;
using UnityEngine;

public class VisionOverlayRenderer : MonoBehaviour
{
    public static VisionOverlayRenderer Instance { get; private set; }

    private readonly List<IVisionSource> visionSources = new();

    private bool isActive;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        GameStateManager.Instance.OnStateChanged += OnStateChanged;
        isActive = GameStateManager.Instance.CurrentState == GameState.PlayMode;
    }

    private void OnDestroy()
    {
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= OnStateChanged;
    }

    private void OnStateChanged(GameState state)
    {
        isActive = state == GameState.PlayMode;
        if (!isActive) visionSources.Clear();
    }

    public void RegisterSource(IVisionSource source)
    {
        if (!isActive) return;
        visionSources.Add(source);
    }

    public void UnregisterSource(IVisionSource source)
    {
        visionSources.Remove(source);
    }


    public void Refresh()
    {
        if (!isActive || VisionOverlayRendererFeature.Instance == null) return;
        var allTiles = new List<Vector2Int>();
        foreach (var source in visionSources)
            allTiles.AddRange(source.GetVisibleTiles());

        VisionOverlayRendererFeature.Instance.SetVisibleTiles(allTiles);
    }

    public void Clear()
    {
        if (!isActive || VisionOverlayRendererFeature.Instance == null) return;
        VisionOverlayRendererFeature.Instance.SetVisibleTiles(System.Array.Empty<Vector2Int>());
    }

}
