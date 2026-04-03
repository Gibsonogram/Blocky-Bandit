using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using static UnityEngine.Rendering.RenderGraphModule.Util.RenderGraphUtils;

public class GridOverlayRendererFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader gridShader;
    [SerializeField] private Color  gridColor = new Color(1f, 1f, 1f, 0.3f); // blue color
    [SerializeField] private float lineThickness = 0.04f;
    [SerializeField] private float tileSize = 1f;
    [SerializeField] private Vector2 gridOffset = new Vector2(0.5f, 0.5f);

    // Opacity setter for each layer
    [Range(0f, 1f)]
    [SerializeField] private float Opacity = 1f;

    private Material gridMaterial;
    private GridOverlayPass renderPass;

    public static GridOverlayRendererFeature Instance { get; private set; }

    public void SetOpacity(float value) => Opacity = Mathf.Clamp01(value);

    public override void Create()
    {
        Instance = this;
        if (gridShader == null)
        {
            return;
        }
        gridMaterial = CoreUtils.CreateEngineMaterial(gridShader);
        renderPass = new GridOverlayPass(gridMaterial);
        renderPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }


    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (gridMaterial == null || renderPass == null)
        {
            return;
        }

        if (renderingData.cameraData.cameraType == CameraType.Preview ||
            renderingData.cameraData.cameraType == CameraType.Reflection)
        {
            return;
        }

        renderPass.UpdateProperties(gridColor, lineThickness, tileSize, gridOffset, Opacity);
        renderer.EnqueuePass(renderPass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(gridMaterial);
    }

    private class GridOverlayPass : ScriptableRenderPass
    {
        private readonly Material material;
        private Color color;
        private float thickness;
        private float tileSize;
        private float opacity;
        private Vector2 offset;

        private static readonly int ColorId     = Shader.PropertyToID("_GridColor");
        private static readonly int ThicknessId = Shader.PropertyToID("_LineThickness");
        private static readonly int TileSizeId  = Shader.PropertyToID("_TileSize");
        private static readonly int OffsetId    = Shader.PropertyToID("_GridOffset");
        private static readonly int OpacityId   = Shader.PropertyToID("_Opacity");
        private static readonly int ClipToWorld = Shader.PropertyToID("_ClipToWorld");

        public GridOverlayPass(Material mat)
        {
            material = mat;
            profilingSampler = new ProfilingSampler("Grid Overlay");
        }

        public void UpdateProperties(Color c, float t, float ts, Vector2 off, float op)
        {
            color = c;
            thickness = t;
            tileSize = ts;
            offset = off;
            opacity = op;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (!resourcesData.cameraColor.IsValid())
                return;

            material.SetColor(ColorId, color);
            material.SetFloat(ThicknessId, thickness);
            material.SetFloat(TileSizeId, tileSize);
            material.SetVector(OffsetId, new Vector4(offset.x, offset.y, 0f, 0f));
            material.SetFloat(OpacityId, opacity);

            Matrix4x4 clipToWorld = (cameraData.camera.projectionMatrix * cameraData.camera.worldToCameraMatrix).inverse;
            material.SetMatrix(ClipToWorld, clipToWorld);

            // source is null — we are drawing on top, not reading what's behind
            var parameters = new BlitMaterialParameters(TextureHandle.nullHandle, resourcesData.activeColorTexture, material, 0);
            renderGraph.AddBlitPass(parameters, passName: "Grid Overlay Blit");
        }
    }
}
