using System.Collections.Generic;
using UnityEngine;
using static GridUtils;

// Shape used for each danger tile. The actor's serialized indicator shape decides which
// mask is drawn; every shape shares the same pixel radius.
public enum VisionOverlayShape
{
    Circle,
    Diamond,
    Square,
    Octagon,
}

// Owns the vision/danger overlay. Once per turn it walks every registered source and rebuilds
// a single mesh of mask quads - one small indicator per source per danger tile - on a sorting
// layer below the actors. Being a mesh, the tint sits on the floor and under the sprites, so a
// crate or the player standing in danger is never tinted itself; the grid overlay still draws
// above everything.
//
// Sources push their tiles into a shared indicator list as they scan, so a refresh allocates
// nothing. Overlapping cones are never merged: each gets its own quad, so their fills stack
// where they meet and both outlines are drawn, showing the player that two different actors
// are watching the same tile.
public class VisionOverlayRenderer : MonoBehaviour, IVisionTileSink
{
    // The overlay's complete look lives in one shared Resources asset, so editing the style
    // from any level's Inspector changes every level. Player-facing opacity is a separate
    // global setting (SaveManager/PlayerPrefs); indicator shapes come from the actors.
    public const string StyleResourceName = "VisionOverlayStyle";
    private static VisionOverlayStyle style;

    public static VisionOverlayStyle Style
    {
        get
        {
            if (style == null)
            {
                style = Resources.Load<VisionOverlayStyle>(StyleResourceName);
                if (style == null)
                    style = ScriptableObject.CreateInstance<VisionOverlayStyle>();
            }

            return style;
        }
    }

    // Drops the cached asset reference so the next access re-reads Resources.
    public static void ReloadStyle() => style = null;

    // Master multiplier for the whole overlay. The player-facing value lives in PlayerPrefs
    // (SaveManager.OverlayOpacity) and is the authority; this copy is what the shader is
    // currently scaled by. Tweak it game-wide from the in-game Options menu or from this
    // script's custom Inspector.
    private float globalOpacity = 1f;

    public static VisionOverlayRenderer Instance { get; private set; }

    private readonly List<IVisionSource> visionSources = new();

    // A single danger indicator to draw: one source covering one tile. Plain data, so a
    // refresh keeps re-using the list instead of allocating.
    private struct Indicator
    {
        public Vector2Int tile;
        public VisionOverlayShape shape;
        public bool lethal;
    }

    // One entry per source per danger tile, pushed in while the sources scan. Keeping them
    // separate - rather than merging a tile down to one shape - is what lets two overlapping
    // cones both draw: their fills sum under additive blending and both outlines are plotted.
    private readonly List<Indicator> indicators = new();

    private VisionSeverity currentSeverity;
    private VisionOverlayShape currentShape;
    private bool isActive;

    // One quad per indicator, sampling a shared pixel-art mask atlas.
    private Mesh tintMesh;
    private MeshRenderer tintRenderer;
    private Transform tintTransform;
    private bool meshReady;
    // Set whenever the indicator list or the atlas changed. The mesh is rebuilt once in
    // LateUpdate, so a turn that repaints several times (a mine arming mid-turn, an actor
    // falling into a hole, the end-of-turn refresh) still uploads exactly one mesh, and the
    // rendered result is the last request of the frame - which is what was drawn before.
    private bool meshDirty;
    private readonly List<Vector3> vertices = new();
    private readonly List<Color> colors = new();
    private readonly List<Vector2> uvs = new();
    private readonly List<int> triangles = new();

    // The four corner offsets of one indicator quad, in the overlay's local space. They are
    // identical for every indicator at a given mask size, so they are derived once per build
    // instead of four InverseTransformPoint calls per tile.
    private readonly Vector3[] quadCorners = new Vector3[4];
    // Authored fill colours converted to linear once per build, not once per indicator.
    private Color warningFill;
    private Color lethalFill;

    // Shape masks are rasterized at one texel per art pixel, so the outline is always exactly
    // one pixel thick and a convex corner never gets a doubled diagonal pixel. The atlas uses
    // R for the fill and G for the outline.
    //
    // Alignment matters as much as the mask itself: a tile centre (n + 0.5) sits on an art
    // pixel *boundary*, so a mask with an odd number of texels is centred on a texel and every
    // one of its texel edges falls half a pixel off the pixel grid. Point sampling then decides
    // each screen pixel at a texel boundary by rounding, which drops or duplicates a column of
    // the outline on one side only - the shape renders skewed and stops being symmetric. The
    // mask therefore always holds an even number of texels, sampled at texel centres, so every
    // texel edge lands exactly on the pixel grid and each texel covers exactly one art pixel.
    private const int PixelsPerUnit = 32;       // matches the tile sprites
    private const int ShapeCount = 4;           // Circle, Diamond, Square, Octagon
    private const int OutlinePixels = 1;

    // Every Mesh buffer setter recalculates the bounds by default; BuildMesh uploads five of
    // them, so they all defer to the single RecalculateBounds() at the end of the build.
    private const UnityEngine.Rendering.MeshUpdateFlags KeepBounds =
        UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds;

    private static readonly int MaskTextureId = Shader.PropertyToID("_MainTex");
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OverlayOpacityId = Shader.PropertyToID("_OverlayOpacity");
    private static readonly int UseOutlineId = Shader.PropertyToID("_UseOutline");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");

    private Texture2D shapeAtlas;
    private int shapeAtlasRadius = -1;          // fill radius in art pixels
    private int shapeMaskSize;                  // even, includes the 1-pixel outline
    private MaterialPropertyBlock rendererProperties;

    // The mesh is drawn twice from the same triangles: submesh 0 is the fill (additive, so
    // overlapping indicators add up) and submesh 1 is the outline (alpha blended, and therefore
    // on top of every fill). Both derive from the style's material, so swapping that material's
    // shader still drives the overlay.
    private Material tintTemplate;
    private Material fillMaterial;
    private Material outlineMaterial;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // The saved player opacity is the authority; every level starts from the same value.
        globalOpacity = SaveManager.OverlayOpacity;

        EnsureTintRenderer();
    }

    private void Start()
    {
        GameStateManager.Instance.OnStateChanged += OnStateChanged;
        isActive = GameStateManager.Instance.CurrentState == GameState.PlayMode;
    }

    private void OnDestroy()
    {
        // The overlay lives in the level scene, but the managers that call it (TurnManager,
        // LevelManager, the actors) live in the Persistent scene and keep calling
        // Instance?.Clear() after a level unloads. Unity's ?. does not honour the destroyed
        // check - the managed reference survives - so a stale Instance would run Clear() on a
        // destroyed object, throw out of GameStateManager.ChangeState and abort every
        // remaining state-change subscriber (the level select never opens).
        if (Instance == this) Instance = null;

        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= OnStateChanged;
        if (tintMesh != null) Destroy(tintMesh);
        if (shapeAtlas != null) Destroy(shapeAtlas);
        ReleaseTintMaterials();
    }

    // Keeps the runtime opacity valid. The mesh does not depend on it - the shader scales the
    // tint with _OverlayOpacity - so changing the value costs one property block, not a rebuild.
    private void OnValidate()
    {
        globalOpacity = Mathf.Clamp01(globalOpacity);
        UpdateRendererProperties();
    }

    public void SetOpacity(float value)
    {
        globalOpacity = Mathf.Clamp01(value);
        UpdateRendererProperties();
    }

    // One-line global control: VisionOverlayRenderer.SetGlobalOpacity(0.25f);
    public static void SetGlobalOpacity(float value) => Instance?.SetOpacity(value);

    // Re-reads the saved Options value. The custom Inspector uses this so its control and
    // the in-game menu drive the same live overlay.
    public static void ApplySavedSettings() => Instance?.ApplySettings();

    private void ApplySettings()
    {
        globalOpacity = SaveManager.OverlayOpacity;
        UpdateRendererProperties();
        SetRadius(SaveManager.OverlayRadius);
    }

    // Indicator size is a player preference, not part of the authored look: changing it
    // re-rasterizes the mask atlas at the new whole-pixel radius and repaints.
    public void SetRadius(float radius)
    {
        if (shapeAtlas != null && shapeAtlasRadius == RadiusToPixels(radius))
            return;

        EnsureShapeAtlas(radius);
        UpdateRendererProperties();
        RebuildIfReady();
    }

    public static void SetGlobalRadius(float radius) => Instance?.SetRadius(radius);

    private static int RadiusToPixels(float radius) =>
        Mathf.Max(1, Mathf.RoundToInt(radius * PixelsPerUnit));

    private void RebuildIfReady()
    {
        if (meshReady && isActive) MarkMeshDirty();
    }

    // The single place the mesh is uploaded. Every refresh request this frame collapses into
    // one build, before rendering, so the drawn mesh is identical to the immediate rebuild
    // the callers used to trigger (the last request of the frame always won).
    private void LateUpdate()
    {
        if (!meshDirty) return;
        BuildMesh();
    }

    private void MarkMeshDirty() => meshDirty = true;

    // Re-applies the shared style to this overlay: materials, sorting and mesh. Used when the
    // global style asset changes.
    public void ApplyStyle()
    {
        if (tintRenderer != null)
        {
            Material template = ResolveTintMaterial();
            if (template != null)
                EnsureTintMaterials(template);

            tintRenderer.sortingLayerName = Style.sortingLayer;
            tintRenderer.sortingOrder = Style.sortingOrder;
        }

        EnsureShapeAtlas(SaveManager.OverlayRadius);
        UpdateRendererProperties();
        RebuildIfReady();
    }

    // Rebakes every loaded overlay after the shared style asset changes. In play mode this
    // updates the live mesh immediately; in edit mode there is no mesh to rebuild.
    public static void ApplyStyleGlobally()
    {
        var overlays = FindObjectsByType<VisionOverlayRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < overlays.Length; i++)
            overlays[i].ApplyStyle();
    }

    // Returning to play (from pause) repaints immediately instead of waiting for the next
    // completed turn; leaving play clears the overlay through Clear().
    private void OnStateChanged(GameState state)
    {
        bool wasActive = isActive;
        isActive = state == GameState.PlayMode;
        if (isActive && !wasActive)
            Refresh();
    }

    public void RegisterSource(IVisionSource source)
    {
        // Always accept the source: Awake/Start ordering across scenes means a source can
        // register before this overlay's Start has read the game state. Rendering still
        // respects isActive, so registering outside play mode is harmless.
        visionSources.Add(source);
    }

    public void UnregisterSource(IVisionSource source)
    {
        visionSources.Remove(source);
    }

    // IVisionTileSink: sources call this while scanning. Refresh() sets currentShape and
    // currentSeverity just before each source, so an indicator is tagged without every source
    // passing its own shape and colour along.
    public void Add(Vector2Int tile)
    {
        indicators.Add(new Indicator
        {
            tile = tile,
            shape = currentShape,
            lethal = currentSeverity == VisionSeverity.Lethal,
        });
    }

    public void Refresh()
    {
        if (!isActive) return;

        indicators.Clear();

        foreach (IVisionSource source in visionSources)
        {
            // A source destroyed earlier this turn can still be in the list until OnDestroy
            // runs (Destroy is deferred); it must not be asked to scan a dead object.
            if (source is Object obj && obj == null) continue;

            currentSeverity = source.Severity;
            currentShape = source.Shape;
            source.CollectDangerTiles(this);
        }

        MarkMeshDirty();
    }

    public void Clear()
    {
        indicators.Clear();
        MarkMeshDirty();
    }

    private static Material ResolveTintMaterial() =>
        Style.tintMaterial != null ? Style.tintMaterial : Resources.Load<Material>("VisionTileTint");

    // Builds the two draw materials from the style's material. Only the blend state and the
    // mask channel differ; everything shared (atlas, outline colour, opacity) rides on the
    // renderer's property block so both submeshes stay in one draw group.
    //
    // The fill is alpha blended rather than added: adding can only brighten, so the dark
    // lethal red barely moved the floor and read as "the warning colour is still there".
    // Alpha blending keeps both authored colours legible, and overlapping indicators still
    // stack because each one blends over the result of the last (0.6 -> 0.84 -> ...).
    private void EnsureTintMaterials(Material template)
    {
        if (tintRenderer == null) return;
        if (fillMaterial != null && tintTemplate == template) return;

        ReleaseTintMaterials();
        tintTemplate = template;

        fillMaterial = CreateTintMaterial(template, "VisionTintFill", 0f,
            UnityEngine.Rendering.BlendMode.SrcAlpha, UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

        // The outline is drawn from the second submesh so it lands on top of every fill; that
        // is what lets two overlapping cones show both outlines instead of the top fill
        // erasing the lower one.
        outlineMaterial = CreateTintMaterial(template, "VisionTintOutline", 1f,
            UnityEngine.Rendering.BlendMode.SrcAlpha, UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

        tintRenderer.sharedMaterials = new[] { fillMaterial, outlineMaterial };
    }

    private static Material CreateTintMaterial(Material template, string name, float useOutline,
        UnityEngine.Rendering.BlendMode src, UnityEngine.Rendering.BlendMode dst)
    {
        var material = new Material(template) { name = name };
        material.SetFloat(UseOutlineId, useOutline);
        material.SetFloat(SrcBlendId, (float)src);
        material.SetFloat(DstBlendId, (float)dst);
        return material;
    }

    private void ReleaseTintMaterials()
    {
        DestroyRuntimeObject(fillMaterial);
        DestroyRuntimeObject(outlineMaterial);
        fillMaterial = null;
        outlineMaterial = null;
        tintTemplate = null;
    }

    // Materials created at runtime are not owned by any asset, so they have to be freed by
    // hand - with Destroy in play mode and DestroyImmediate in the editor.
    private static void DestroyRuntimeObject(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    private void EnsureTintRenderer()
    {
        // Re-enter if a previous mesh was destroyed under us (level unload), so the overlay
        // can rebuild rather than stay permanently blank.
        if (meshReady && tintMesh != null) return;
        meshReady = false;

        Material template = ResolveTintMaterial();
        if (template == null)
        {
            Debug.LogWarning("VisionOverlay: no tint material found (Resources/VisionTileTint); overlay disabled.", this);
            return;
        }

        var go = new GameObject("VisionTint");
        go.transform.SetParent(transform, false);
        tintTransform = go.transform;

        var filter = go.AddComponent<MeshFilter>();
        tintRenderer = go.AddComponent<MeshRenderer>();
        EnsureTintMaterials(template);
        tintRenderer.sortingLayerName = Style.sortingLayer;
        tintRenderer.sortingOrder = Style.sortingOrder;
        tintRenderer.enabled = false;

        tintMesh = new Mesh { name = "VisionTint" };
        tintMesh.MarkDynamic();
        filter.sharedMesh = tintMesh;

        meshReady = true;
        EnsureShapeAtlas(SaveManager.OverlayRadius);
        UpdateRendererProperties();
    }

    private void BuildMesh()
    {
        meshDirty = false;

        // A caller can outlive the level (see OnDestroy): the mesh is destroyed with the
        // scene, so a stale Clear()/Refresh() must be a no-op instead of an exception.
        if (!meshReady || tintMesh == null)
        {
            meshReady = false;
            return;
        }

        vertices.Clear();
        colors.Clear();
        uvs.Clear();
        triangles.Clear();

        if (indicators.Count > 0 && shapeAtlas != null)
        {
            // The fill colours and the quad's four local corner offsets are the same for every
            // indicator, so they are derived once per build rather than once per tile.
            warningFill = TintColor(Style.warningColor);
            lethalFill = TintColor(Style.lethalColor);
            CacheQuadCorners();

            for (int i = 0; i < indicators.Count; i++)
            {
                Indicator indicator = indicators[i];
                AddTileQuad(indicator.tile, indicator.lethal, indicator.shape);
            }
        }

        tintMesh.Clear();
        if (vertices.Count == 0)
        {
            tintRenderer.enabled = false;
            return;
        }

        // Bounds are deferred to the single RecalculateBounds below instead of being recomputed
        // by each of these five buffer uploads.
        tintMesh.SetVertices(vertices, 0, vertices.Count, KeepBounds);
        tintMesh.SetColors(colors, 0, colors.Count, KeepBounds);
        tintMesh.SetUVs(0, uvs, 0, uvs.Count, KeepBounds);

        // Two submeshes over the same triangles: the fill draws first and the outline second,
        // so every outline sits above every fill no matter which actor produced it.
        tintMesh.subMeshCount = 2;
        tintMesh.SetTriangles(triangles, 0, calculateBounds: false);
        tintMesh.SetTriangles(triangles, 1, calculateBounds: false);

        tintMesh.RecalculateBounds();
        tintRenderer.enabled = true;
    }

    // The colour a fill is drawn with: the authored hue in linear space, keeping the authored
    // alpha. The shader scales the whole thing by the single global opacity.
    private static Color TintColor(Color source)
    {
        Color color = source.linear;
        color.a = Mathf.Clamp01(source.a);
        return color;
    }

    // The mask quad covers one art pixel per texel, including the fixed one-pixel outline.
    // Its centre texel edge falls on the tile centre, which is an art-pixel boundary, so the
    // quad lands exactly on the pixel grid and every texel covers exactly one pixel. Sizing it
    // once per build keeps AddTileQuad to a single centre transform plus four adds.
    private void CacheQuadCorners()
    {
        float extent = shapeMaskSize / (2f * PixelsPerUnit);
        quadCorners[0] = tintTransform.InverseTransformVector(new Vector3(-extent, -extent, 0f));
        quadCorners[1] = tintTransform.InverseTransformVector(new Vector3(extent, -extent, 0f));
        quadCorners[2] = tintTransform.InverseTransformVector(new Vector3(extent, extent, 0f));
        quadCorners[3] = tintTransform.InverseTransformVector(new Vector3(-extent, extent, 0f));
    }

    private void AddTileQuad(Vector2Int tile, bool lethal, VisionOverlayShape shape)
    {
        if (shapeAtlas == null) return;

        Color color = lethal ? lethalFill : warningFill;

        int shapeIndex = Mathf.Clamp((int)shape, 0, ShapeCount - 1);
        float u0 = shapeIndex * shapeMaskSize / (float)shapeAtlas.width;
        float u1 = (shapeIndex + 1) * shapeMaskSize / (float)shapeAtlas.width;

        Vector3 center = tintTransform.InverseTransformPoint(GridToWorld(tile));

        int start = vertices.Count;

        vertices.Add(center + quadCorners[0]);
        colors.Add(color);
        uvs.Add(new Vector2(u0, 0f));

        vertices.Add(center + quadCorners[1]);
        colors.Add(color);
        uvs.Add(new Vector2(u1, 0f));

        vertices.Add(center + quadCorners[2]);
        colors.Add(color);
        uvs.Add(new Vector2(u1, 1f));

        vertices.Add(center + quadCorners[3]);
        colors.Add(color);
        uvs.Add(new Vector2(u0, 1f));

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private void EnsureShapeAtlas(float radiusUnits)
    {
        int radius = RadiusToPixels(radiusUnits);
        if (shapeAtlas != null && shapeAtlasRadius == radius) return;

        if (shapeAtlas != null) Destroy(shapeAtlas);

        shapeAtlasRadius = radius;

        // Polygons are drawn at a radius midway between inscribed and circumscribed (see
        // ShapeRadiusScale), so the mask has to hold the largest of them - the square - plus
        // one outline pixel on each side. Computed once per rebuild, never per indicator.
        float maxScale = 1f;
        for (int shape = 0; shape < ShapeCount; shape++)
            maxScale = Mathf.Max(maxScale, ShapeRadiusScale((VisionOverlayShape)shape));

        int halfWidth = Mathf.CeilToInt(radius * maxScale) + OutlinePixels;
        shapeMaskSize = halfWidth * 2;                  // even; every texel edge is on the pixel grid
        int size = shapeMaskSize;

        var texture = new Texture2D(size * ShapeCount, size, TextureFormat.RGBA32, false, true);
        texture.name = "VisionShapeAtlas";
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.anisoLevel = 0;

        var pixels = new Color32[texture.width * texture.height];
        var fill = new bool[size * size];
        var outline = new bool[size * size];

        for (int shape = 0; shape < ShapeCount; shape++)
        {
            VisionOverlayShape current = (VisionOverlayShape)shape;
            BuildShapeMask(current, radius * ShapeRadiusScale(current), size, fill, outline);

            int offsetX = shape * size;
            for (int y = 0; y < size; y++)
            {
                int sourceRow = y * size;
                int targetRow = y * texture.width + offsetX;
                for (int x = 0; x < size; x++)
                {
                    int i = sourceRow + x;
                    pixels[targetRow + x] = fill[i]
                        ? new Color32(255, 0, 0, 255)
                        : outline[i]
                            ? new Color32(0, 255, 0, 255)
                            : new Color32(0, 0, 0, 0);
                }
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        shapeAtlas = texture;
    }

    private static void BuildShapeMask(VisionOverlayShape shape, float radius, int size, bool[] fill, bool[] outline)
    {
        System.Array.Clear(fill, 0, fill.Length);
        System.Array.Clear(outline, 0, outline.Length);

        // Texel centres are half a texel off the tile centre, which is what keeps the mask
        // symmetric across both axes for an even texel count: x and (size - 1 - x) evaluate
        // mirrored coordinates, so the fill and the outline below are mirror images by
        // construction rather than by luck of the rounding.
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            float dy = y + 0.5f - half;
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - half;
                fill[y * size + x] = InsideShape(shape, dx, dy, radius);
            }
        }

        // One pixel of outline. A pixel joins when it touches the fill orthogonally. The square
        // is the only shape whose silhouette has real corners, so it is the only one that also
        // takes the outside-corner pixel (diagonally adjacent to fill with neither in-between
        // cell filled); on a staircase edge - circle, diamond, octagon - that pixel would be the
        // diagonal "double" we do not want.
        bool cornerPixels = shape == VisionOverlayShape.Square;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                if (fill[i]) continue;

                bool orthogonal =
                    (x > 0 && fill[i - 1]) ||
                    (x < size - 1 && fill[i + 1]) ||
                    (y > 0 && fill[i - size]) ||
                    (y < size - 1 && fill[i + size]);

                outline[i] = orthogonal || (cornerPixels && ClosesCorner(fill, size, x, y));
            }
        }
    }

    // True when this empty cell sits diagonally outside a filled cell that neither of the two
    // cells between them is filled into - an outside corner of the silhouette.
    private static bool ClosesCorner(bool[] fill, int size, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy += 2)
        {
            int ny = y + dy;
            if (ny < 0 || ny >= size) continue;

            for (int dx = -1; dx <= 1; dx += 2)
            {
                int nx = x + dx;
                if (nx < 0 || nx >= size) continue;

                if (!fill[ny * size + nx]) continue;
                if (fill[y * size + nx] || fill[ny * size + x]) continue;
                return true;
            }
        }

        return false;
    }

    // A polygon inscribed in the circle (its vertices on the circle) reads smaller than the
    // circle, and one circumscribed around it reads bigger. Sizing its circumradius so that the
    // middle of the two radii lands on the circle - scale = 2 / (1 + cos(pi / n)) - makes every
    // shape read at the same size. Called once per shape per atlas rebuild.
    private static float ShapeRadiusScale(VisionOverlayShape shape)
    {
        switch (shape)
        {
            case VisionOverlayShape.Square:
            case VisionOverlayShape.Diamond:
                return 2f / (1f + 0.70710678f);     // n = 4, cos(45 degrees)

            case VisionOverlayShape.Octagon:
                return 2f / (1f + 0.92387953f);     // n = 8, cos(22.5 degrees)

            default: // Circle: inscribed and circumscribed radii are the same.
                return 1f;
        }
    }

    private static bool InsideShape(VisionOverlayShape shape, float dx, float dy, float radius)
    {
        float ax = Mathf.Abs(dx);
        float ay = Mathf.Abs(dy);

        switch (shape)
        {
            case VisionOverlayShape.Diamond:
                return ax + ay <= radius;

            case VisionOverlayShape.Square:
            {
                // Corners on the given circumradius; the caller scales that radius to the
                // midway size so the square reads as large as the circle.
                float halfSide = radius * 0.70710678f;
                return ax <= halfSide && ay <= halfSide;
            }

            case VisionOverlayShape.Octagon:
            {
                // Regular octagon with vertices at 22.5 + k * 45 degrees.
                float apothem = radius * 0.92387953f; // radius * cos(22.5 degrees)
                return ax <= apothem &&
                       ay <= apothem &&
                       ax + ay <= apothem * 1.41421356f;
            }

            default: // Circle
                return dx * dx + dy * dy <= radius * radius;
        }
    }

    private void UpdateRendererProperties()
    {
        if (tintRenderer == null || shapeAtlas == null) return;

        if (rendererProperties == null)
            rendererProperties = new MaterialPropertyBlock();

        // The master opacity rides on the property block, so a slider drag costs one block
        // instead of a mesh rebuild; both submeshes scale their alpha by it.
        rendererProperties.SetTexture(MaskTextureId, shapeAtlas);
        rendererProperties.SetColor(OutlineColorId, Style.outlineColor.linear);
        rendererProperties.SetFloat(OverlayOpacityId, globalOpacity);
        tintRenderer.SetPropertyBlock(rendererProperties);
    }
}
