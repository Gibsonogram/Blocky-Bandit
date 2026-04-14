using System.Collections.Generic;
using UnityEngine;

public class VisionOverlayRenderer : MonoBehaviour
{
    public static VisionOverlayRenderer Instance { get; private set; }

    private readonly List<IVisionSource> visionSources = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void RegisterSource(IVisionSource source) => visionSources.Add(source);
    public void UnregisterSource(IVisionSource source) => visionSources.Remove(source);

    public void Refresh()
    {
        var allTiles = new List<Vector2Int>();
        foreach (var source in visionSources)
            allTiles.AddRange(source.GetVisibleTiles());

        VisionOverlayRendererFeature.Instance.SetVisibleTiles(allTiles);
    }
}
