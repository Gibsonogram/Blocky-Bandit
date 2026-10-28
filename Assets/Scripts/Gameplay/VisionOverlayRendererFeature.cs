using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class VisionOverlayRendererFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader visionShader;
    [SerializeField] private Color dotColor = new Color(1f, 1f, 0f, 0.9f);
    [SerializeField] private float dotRadius = 0.18f;
    [SerializeField] private float tileSize = 1f;
    [SerializeField] private Vector2 gridOffset = new Vector2(0.5f, 0.5f);

    // Safety valve: bounds beyond this are skipped rather than silently allocating a huge
    // buffer. 4M tiles is ~500KB of bitset and far past any real level.
    [SerializeField] private int maxTiles = 4 * 1024 * 1024;

    [Range(0f, 1f)]
    [SerializeField] private float Opacity = 1f;

    public static VisionOverlayRendererFeature Instance { get; private set; }

    public void SetOpacity(float value) => Opacity = Mathf.Clamp01(value);

    private Material material;
    private VisionOverlayPass renderPass;
    private readonly List<Vector2Int> visibleTiles = new();

    // The tile set is packed into a bitset over the grid bounds instead of being uploaded
    // as a position list. The shader then answers "is this tile visible?" with one bit test
    // per neighbour, instead of looping over every tile for every pixel.
    private uint[] tileBits = System.Array.Empty<uint>();
    private Vector2Int gridMin;
    private Vector2Int gridSize;
    private bool dirty = true;

    public void SetVisibleTiles(IEnumerable<Vector2Int> tiles)
    {
        // Only mark dirty when the set actually changes; the caller refreshes every turn.
        bool changed = false;
        int i = 0;
        foreach (Vector2Int tile in tiles)
        {
            if (i < visibleTiles.Count)
            {
                if (visibleTiles[i] != tile) { visibleTiles[i] = tile; changed = true; }
            }
            else
            {
                visibleTiles.Add(tile);
                changed = true;
            }
            i++;
        }

        if (i != visibleTiles.Count)
        {
            visibleTiles.RemoveRange(i, visibleTiles.Count - i);
            changed = true;
        }

        if (changed) dirty = true;
    }

    public override void Create()
    {
        Instance = this;
        if (visionShader == null) return;
        material = CoreUtils.CreateEngineMaterial(visionShader);
        renderPass = new VisionOverlayPass(material);
        renderPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null || renderPass == null) return;
        if (renderingData.cameraData.cameraType == CameraType.Preview ||
            renderingData.cameraData.cameraType == CameraType.Reflection) return;

        if (dirty)
        {
            RebuildBitset();
            dirty = false;
        }

        renderPass.UpdateOverlay(tileBits, gridMin, gridSize, dotColor, dotRadius, tileSize, gridOffset, Opacity);
        renderer.EnqueuePass(renderPass);
    }

    // Packs the visible tiles into a bitset spanning their bounds. Runs only when the tile
    // set changes (at most once a turn), never every frame.
    private void RebuildBitset()
    {
        if (visibleTiles.Count == 0)
        {
            gridMin = Vector2Int.zero;
            gridSize = Vector2Int.zero;
            if (tileBits.Length != 0) tileBits = System.Array.Empty<uint>();
            return;
        }

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Vector2Int tile in visibleTiles)
        {
            if (tile.x < minX) minX = tile.x;
            if (tile.y < minY) minY = tile.y;
            if (tile.x > maxX) maxX = tile.x;
            if (tile.y > maxY) maxY = tile.y;
        }

        // Pad by one so the shader's 3x3 neighbourhood probe always stays in range.
        minX -= 1; minY -= 1; maxX += 1; maxY += 1;

        long width = (long)maxX - minX + 1;
        long height = (long)maxY - minY + 1;
        if (width * height > maxTiles)
        {
            Debug.LogWarning($"VisionOverlay: tile bounds {width}x{height} exceed maxTiles; overlay skipped.");
            gridMin = Vector2Int.zero;
            gridSize = Vector2Int.zero;
            tileBits = System.Array.Empty<uint>();
            return;
        }

        gridMin = new Vector2Int(minX, minY);
        gridSize = new Vector2Int((int)width, (int)height);

        int words = ((int)(width * height) + 31) >> 5;
        // Reuse the array when the size is unchanged; only allocate when the grid grows.
        if (tileBits.Length != words) tileBits = new uint[words];
        else System.Array.Clear(tileBits, 0, words);

        int stride = (int)width;
        foreach (Vector2Int tile in visibleTiles)
        {
            int index = (tile.y - minY) * stride + (tile.x - minX);
            tileBits[index >> 5] |= 1u << (index & 31);
        }
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
        renderPass?.DisposeOverlay();
    }
}

internal class VisionOverlayPass : ScriptableRenderPass
{
    private readonly Material material;

    // Kept alive across frames: allocating a ComputeBuffer every frame was a GPU sync
    // point and the single biggest cost in this pass. Reallocated only when it grows.
    private ComputeBuffer tileBuffer;
    private int bufferWords = -1;

    private Color dotColor;
    private float dotRadius, tileSize;
    private Vector2 gridOffset;
    private Vector2Int gridMin, gridSize;
    private bool hasTiles;

    private static readonly int TileBitsId    = Shader.PropertyToID("_TileBits");
    private static readonly int GridMinId     = Shader.PropertyToID("_GridMin");
    private static readonly int GridSizeId    = Shader.PropertyToID("_GridSize");
    private static readonly int DotColorId    = Shader.PropertyToID("_DotColor");
    private static readonly int DotRadiusId   = Shader.PropertyToID("_DotRadius");
    private static readonly int TileSizeId    = Shader.PropertyToID("_TileSize");
    private static readonly int GridOffsetId  = Shader.PropertyToID("_GridOffset");
    private static readonly int ClipToWorldId = Shader.PropertyToID("_ClipToWorld");

    public VisionOverlayPass(Material mat)
    {
        material = mat;
        profilingSampler = new ProfilingSampler("Vision Overlay");
    }

    public void UpdateOverlay(uint[] words, Vector2Int min, Vector2Int size, Color color,
        float radius, float ts, Vector2 offset, float opacity)
    {
        dotColor = color;
        dotColor.a = Mathf.Clamp01(color.a * opacity);
        dotRadius = radius;
        tileSize = ts;
        gridOffset = offset;
        gridMin = min;
        gridSize = size;

        hasTiles = words != null && words.Length > 0 && size.x > 0 && size.y > 0;
        if (!hasTiles) return;

        if (tileBuffer == null || bufferWords != words.Length)
        {
            tileBuffer?.Release();
            tileBuffer = new ComputeBuffer(words.Length, sizeof(uint));
            bufferWords = words.Length;
        }

        tileBuffer.SetData(words);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (!hasTiles || tileBuffer == null) return;

        var resourcesData = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();
        if (!resourcesData.cameraColor.IsValid()) return;

        material.SetColor(DotColorId, dotColor);
        material.SetFloat(DotRadiusId, dotRadius);
        material.SetFloat(TileSizeId, tileSize);
        material.SetVector(GridOffsetId, new Vector4(gridOffset.x, gridOffset.y, 0, 0));
        material.SetBuffer(TileBitsId, tileBuffer);
        material.SetVector(GridMinId, new Vector4(gridMin.x, gridMin.y, 0, 0));
        material.SetVector(GridSizeId, new Vector4(gridSize.x, gridSize.y, 0, 0));

        Matrix4x4 clipToWorld = (cameraData.camera.projectionMatrix * cameraData.camera.worldToCameraMatrix).inverse;
        material.SetMatrix(ClipToWorldId, clipToWorld);

        var parameters = new RenderGraphUtils.BlitMaterialParameters(
            TextureHandle.nullHandle, resourcesData.activeColorTexture, material, 0);
        renderGraph.AddBlitPass(parameters, passName: "Vision Overlay Blit");
    }

    public void DisposeOverlay()
    {
        tileBuffer?.Release();
        tileBuffer = null;
        bufferWords = -1;
    }
}
