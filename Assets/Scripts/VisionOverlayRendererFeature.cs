using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class VisionOverlayRendererFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader visionShader;
    [SerializeField] private Color dotColor = new Color(1f, 1f, 0f, 0.5f);
    [SerializeField] private float dotRadius = 0.18f;
    [SerializeField] private float tileSize = 1f;
    [SerializeField] private Vector2 gridOffset = new Vector2(0.5f, 0.5f);

    public static VisionOverlayRendererFeature Instance { get; private set; }

    private Material material;
    private VisionOverlayPass renderPass;
    private readonly List<Vector2Int> visibleTiles = new();

    public void SetVisibleTiles(IEnumerable<Vector2Int> tiles)
    {
        visibleTiles.Clear();
        visibleTiles.AddRange(tiles);
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

        renderPass.UpdateTiles(visibleTiles, dotColor, dotRadius, tileSize, gridOffset);
        renderer.EnqueuePass(renderPass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
        renderPass?.Dispose();
    }

    private class VisionOverlayPass : ScriptableRenderPass
    {
        private readonly Material material;
        private ComputeBuffer tileBuffer;
        private Color dotColor;
        private float dotRadius, tileSize;
        private Vector2 gridOffset;
        private int tileCount;

        private static readonly int TilePositionsId = Shader.PropertyToID("_TilePositions");
        private static readonly int TileCountId     = Shader.PropertyToID("_TileCount");
        private static readonly int DotColorId      = Shader.PropertyToID("_DotColor");
        private static readonly int DotRadiusId     = Shader.PropertyToID("_DotRadius");
        private static readonly int TileSizeId      = Shader.PropertyToID("_TileSize");
        private static readonly int GridOffsetId    = Shader.PropertyToID("_GridOffset");
        private static readonly int ClipToWorldId   = Shader.PropertyToID("_ClipToWorld");

        public VisionOverlayPass(Material mat)
        {
            material = mat;
            profilingSampler = new ProfilingSampler("Vision Overlay");
        }

        public void UpdateTiles(List<Vector2Int> tiles, Color color, float radius, float ts, Vector2 offset)
        {
            dotColor = color;
            dotRadius = radius;
            tileSize = ts;
            gridOffset = offset;
            tileCount = tiles.Count;

            tileBuffer?.Release();
            if (tileCount == 0) return;

            var data = new Vector2[tileCount];
            for (int i = 0; i < tileCount; i++)
                data[i] = new Vector2(tiles[i].x, tiles[i].y);

            tileBuffer = new ComputeBuffer(tileCount, sizeof(float) * 2);
            tileBuffer.SetData(data);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (tileCount == 0) return;

            var resourcesData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (!resourcesData.cameraColor.IsValid()) return;

            material.SetColor(DotColorId, dotColor);
            material.SetFloat(DotRadiusId, dotRadius);
            material.SetFloat(TileSizeId, tileSize);
            material.SetVector(GridOffsetId, new Vector4(gridOffset.x, gridOffset.y, 0, 0));
            material.SetBuffer(TilePositionsId, tileBuffer);
            material.SetInt(TileCountId, tileCount);

            Matrix4x4 clipToWorld = (cameraData.camera.projectionMatrix * cameraData.camera.worldToCameraMatrix).inverse;
            material.SetMatrix(ClipToWorldId, clipToWorld);

            var parameters = new RenderGraphUtils.BlitMaterialParameters(
                TextureHandle.nullHandle, resourcesData.activeColorTexture, material, 0);
            renderGraph.AddBlitPass(parameters, passName: "Vision Overlay Blit");
        }

        public void Dispose()
        {
            tileBuffer?.Release();
            tileBuffer = null;
        }
    }
}

