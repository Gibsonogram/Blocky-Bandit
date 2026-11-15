using System.Collections.Generic;
using UnityEngine;
using static GridUtils;

// Shape used for each danger tile. Every shape is inscribed in the same dotRadius circle,
// so the radius keeps the meaning it has for the circle: the distance from the centre to
// an outer vertex. Diamond puts its points on the axes; Square puts its corners halfway
// between them.
public enum VisionOverlayShape
{
    Circle,
    Diamond,
    Square,
}

// Owns the vision/danger overlay. Once per turn it walks every registered source, counts how
// many cones cover each tile, and rebuilds a single vertex-coloured mesh - one small dot per
// covered tile - on a sorting layer below the actors. Being a mesh, the tint sits on the
// floor and under the sprites, so a crate or the player standing in danger is never tinted
// itself; the grid overlay still draws above everything.
//
// Sources push tiles into this class as they scan, so a refresh allocates nothing and the
// per-tile work is a single increment. Coverage is packed one uint per tile:
//   bits 0-7   warning cones covering the tile
//   bits 8-15  lethal cones covering the tile
public class VisionOverlayRenderer : MonoBehaviour, IVisionTileSink
{
    [Header("Rendering")]
    [Tooltip("Material for the tint mesh. Falls back to Resources/VisionTileTint.")]
    [SerializeField] private Material tintMaterial;
    [Tooltip("Sorting layer for the tint. Must be below the actors and above the floor.")]
    [SerializeField] private string sortingLayer = "WalkInFront";
    [SerializeField] private int sortingOrder = 5;
    [Tooltip("Radius of each danger dot in world units (one tile is 1 unit).")]
    [SerializeField, Range(0.01f, 0.5f)] private float dotRadius = 0.18f;

    [Header("Colors (Apollo palette)")]
    // #E8C170: a tile that will arm a mine, start a chase or count down to a shot.
    [SerializeField] private Color warningColor = new Color(0.9098039f, 0.75686276f, 0.4392157f, 1f);
    // #A53030: a tile the actor's own rules will kill on next turn.
    [SerializeField] private Color lethalColor = new Color(0.64705884f, 0.1882353f, 0.1882353f, 1f);

    [Header("Alpha")]
    // One cone is subtle; each extra cone covering the same tile adds a step, so overlapping
    // sight lines read as denser without needing a third colour. The base is 1 so the master
    // opacity reaching 1 is genuinely fully opaque; extra cones add coverage before the
    // master scale, so overlaps still read when the master is lowered.
    [Range(0f, 1f)] [SerializeField] private float warningBaseAlpha = 1f;
    [Range(0f, 1f)] [SerializeField] private float warningAlphaPerStack = 0.15f;
    [Range(0f, 1f)] [SerializeField] private float warningMaxAlpha = 1f;
    [Range(0f, 1f)] [SerializeField] private float lethalBaseAlpha = 1f;
    [Range(0f, 1f)] [SerializeField] private float lethalAlphaPerStack = 0.10f;
    [Range(0f, 1f)] [SerializeField] private float lethalMaxAlpha = 1f;

    // Master multiplier for the whole overlay. The player-facing value lives in PlayerPrefs
    // (SaveManager.OverlayOpacity) and is the authority: this copy is just what the current
    // mesh was built with. Tweak it game-wide from the in-game Options menu or from this
    // script's custom Inspector.
    private float globalOpacity = 1f;

    // Player-facing dot shape, also stored in SaveManager.OverlayShape.
    private VisionOverlayShape dotShape = VisionOverlayShape.Circle;

    // Small starting window; it doubles only when a cone reaches past the current bounds.
    private const int InitialCoverageSize = 64;
    private const int MaxCoverageTiles = 1024 * 1024;

    public static VisionOverlayRenderer Instance { get; private set; }

    private readonly List<IVisionSource> visionSources = new();

    private uint[] coverage = System.Array.Empty<uint>();
    private Vector2Int coverageMin;
    private Vector2Int coverageSize;

    // Cells written during the current refresh. Clearing only these keeps the refresh
    // proportional to the number of danger tiles rather than the size of the level.
    private readonly List<int> touched = new();

    private VisionSeverity currentSeverity;
    private bool isActive;

    // One quad per covered tile, all in a single draw call.
    private Mesh tintMesh;
    private MeshRenderer tintRenderer;
    private Transform tintTransform;
    private bool meshReady;
    private readonly List<Vector3> vertices = new();
    private readonly List<Color> colors = new();
    private readonly List<int> triangles = new();

    // Dots are built as a triangle fan: a solid inner shape (the whole core is opaque) plus
    // an outer ring that fades to zero alpha. This avoids texture sampling entirely - no
    // minification can thin the centre, so master opacity 1 really is fully opaque.
    private const int CircleSegments = 16;
    private const int PolygonSegments = 4;
    private const float DotSolidFraction = 0.7f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // The saved player settings are the authority, so every level scene starts from the
        // same opacity and shape as the in-game Options menu.
        globalOpacity = SaveManager.OverlayOpacity;
        dotShape = SaveManager.OverlayShape;

        EnsureTintRenderer();
    }

    private void Start()
    {
        GameStateManager.Instance.OnStateChanged += OnStateChanged;
        isActive = GameStateManager.Instance.CurrentState == GameState.PlayMode;
    }

    private void OnDestroy()
    {
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= OnStateChanged;
        if (tintMesh != null) Destroy(tintMesh);
    }

    // Editor-time rebuild so opacity/colour/alphas can be tuned live in the Inspector.
    private void OnValidate()
    {
        globalOpacity = Mathf.Clamp01(globalOpacity);
        if (meshReady && Application.isPlaying && isActive)
            BuildMesh();
    }

    public void SetOpacity(float value)
    {
        globalOpacity = Mathf.Clamp01(value);
        RebuildIfReady();
    }

    public void SetShape(VisionOverlayShape shape)
    {
        if (dotShape == shape) return;

        dotShape = shape;
        RebuildIfReady();
    }

    // One-line global controls: VisionOverlayRenderer.SetGlobalOpacity(0.25f);
    public static void SetGlobalOpacity(float value) => Instance?.SetOpacity(value);
    public static void SetGlobalShape(VisionOverlayShape shape) => Instance?.SetShape(shape);

    // Re-reads the saved Options values. The custom Inspector uses this so its controls and
    // the in-game menu drive the same live overlay.
    public static void ApplySavedSettings() => Instance?.ApplySettings();

    private void ApplySettings()
    {
        globalOpacity = SaveManager.OverlayOpacity;
        dotShape = SaveManager.OverlayShape;
        RebuildIfReady();
    }

    private void RebuildIfReady()
    {
        if (meshReady && isActive) BuildMesh();
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

    // IVisionTileSink: sources call this while scanning. Refresh() sets currentSeverity just
    // before each source, so the tile is counted under the right colour without every source
    // tagging each Add.
    public void Add(Vector2Int tile)
    {
        if (coverageSize.x <= 0) SeedCoverage(tile);
        EnsureCoverageContains(tile);
        if (coverageSize.x <= 0) return;

        int index = (tile.y - coverageMin.y) * coverageSize.x + (tile.x - coverageMin.x);
        uint word = coverage[index];

        // First coverage in this cell this refresh: remember it so the next refresh can clear
        // just the cells that were used.
        if (word == 0u) touched.Add(index);

        int warning = (int)(word & 0xFFu);
        int lethal = (int)((word >> 8) & 0xFFu);
        if (currentSeverity == VisionSeverity.Lethal)
            lethal = Mathf.Min(lethal + 1, 255);
        else
            warning = Mathf.Min(warning + 1, 255);

        coverage[index] = (uint)warning | ((uint)lethal << 8);
    }

    public void Refresh()
    {
        if (!isActive) return;

        ClearCoverage();

        foreach (IVisionSource source in visionSources)
        {
            // A source destroyed earlier this turn can still be in the list until OnDestroy
            // runs (Destroy is deferred); it must not be asked to scan a dead object.
            if (source is Object obj && obj == null) continue;

            currentSeverity = source.Severity;
            source.CollectDangerTiles(this);
        }

        BuildMesh();
    }

    public void Clear()
    {
        ClearCoverage();
        BuildMesh();
    }

    private void ClearCoverage()
    {
        foreach (int index in touched)
            coverage[index] = 0u;
        touched.Clear();
    }

    private void SeedCoverage(Vector2Int tile)
    {
        int half = InitialCoverageSize / 2;
        ReallocateCoverage(
            tile - new Vector2Int(half, half),
            tile + new Vector2Int(InitialCoverageSize - half - 1, InitialCoverageSize - half - 1));
    }

    private void EnsureCoverageContains(Vector2Int tile)
    {
        if (tile.x >= coverageMin.x && tile.y >= coverageMin.y &&
            tile.x < coverageMin.x + coverageSize.x && tile.y < coverageMin.y + coverageSize.y)
            return;

        // Grow in one step to at least double the current extent, so a cone spreading across
        // the level costs a handful of reallocations at most - never one per tile.
        Vector2Int unionMin = Vector2Int.Min(coverageMin, tile);
        Vector2Int unionMax = Vector2Int.Max(coverageMin + coverageSize - Vector2Int.one, tile);
        int width = Mathf.Max(unionMax.x - unionMin.x + 1, coverageSize.x * 2);
        int height = Mathf.Max(unionMax.y - unionMin.y + 1, coverageSize.y * 2);

        int padX = width - (unionMax.x - unionMin.x + 1);
        int padY = height - (unionMax.y - unionMin.y + 1);
        Vector2Int min = new Vector2Int(unionMin.x - padX / 2, unionMin.y - padY / 2);

        ReallocateCoverage(min, min + new Vector2Int(width - 1, height - 1));
    }

    private void ReallocateCoverage(Vector2Int min, Vector2Int max)
    {
        Vector2Int size = max - min + Vector2Int.one;
        if (size.x <= 0 || size.y <= 0) return;

        long tileCount = (long)size.x * size.y;
        if (tileCount > MaxCoverageTiles)
        {
            Debug.LogWarning($"VisionOverlay: coverage bounds {size.x}x{size.y} exceed maxTiles; overlay skipped.");
            return;
        }

        var grown = new uint[size.x * size.y];

        // Growth can happen mid-refresh, so carry over what has already been counted and
        // rebuild the touched list against the new indices.
        if (coverageSize.x > 0 && coverage.Length > 0)
        {
            for (int y = 0; y < coverageSize.y; y++)
            {
                int oldRow = y * coverageSize.x;
                int newRow = (coverageMin.y + y - min.y) * size.x + (coverageMin.x - min.x);
                System.Array.Copy(coverage, oldRow, grown, newRow, coverageSize.x);
            }

            touched.Clear();
            for (int i = 0; i < grown.Length; i++)
                if (grown[i] != 0u) touched.Add(i);
        }

        coverage = grown;
        coverageMin = min;
        coverageSize = size;
    }

    private void EnsureTintRenderer()
    {
        if (meshReady) return;

        if (tintMaterial == null)
            tintMaterial = Resources.Load<Material>("VisionTileTint");

        if (tintMaterial == null)
        {
            Debug.LogWarning("VisionOverlay: no tint material found (Resources/VisionTileTint); overlay disabled.", this);
            return;
        }

        var go = new GameObject("VisionTint");
        go.transform.SetParent(transform, false);
        tintTransform = go.transform;

        var filter = go.AddComponent<MeshFilter>();
        tintRenderer = go.AddComponent<MeshRenderer>();
        tintRenderer.sharedMaterial = tintMaterial;
        tintRenderer.sortingLayerName = sortingLayer;
        tintRenderer.sortingOrder = sortingOrder;
        tintRenderer.enabled = false;

        tintMesh = new Mesh { name = "VisionTint" };
        tintMesh.MarkDynamic();
        filter.sharedMesh = tintMesh;

        meshReady = true;
    }

    private void BuildMesh()
    {
        if (!meshReady) return;

        vertices.Clear();
        colors.Clear();
        triangles.Clear();

        for (int i = 0; i < touched.Count; i++)
        {
            int index = touched[i];
            uint word = coverage[index];
            int warning = (int)(word & 0xFFu);
            int lethal = (int)((word >> 8) & 0xFFu);
            int total = warning + lethal;
            if (total == 0) continue;

            int x = index % coverageSize.x + coverageMin.x;
            int y = index / coverageSize.x + coverageMin.y;
            AddTileDot(x, y, lethal > 0, total);
        }

        tintMesh.Clear();
        if (vertices.Count == 0)
        {
            tintRenderer.enabled = false;
            return;
        }

        tintMesh.SetVertices(vertices);
        tintMesh.SetColors(colors);
        tintMesh.SetTriangles(triangles, 0);
        tintMesh.RecalculateBounds();
        tintRenderer.enabled = true;
    }

    private void AddTileDot(int x, int y, bool lethal, int total)
    {
        Vector3 worldCenter = GridToWorld(new Vector2Int(x, y));

        float baseAlpha = lethal ? lethalBaseAlpha : warningBaseAlpha;
        float stepAlpha = lethal ? lethalAlphaPerStack : warningAlphaPerStack;
        float maxAlpha = lethal ? lethalMaxAlpha : warningMaxAlpha;

        // Scale by the master first, then cap: at master 1 a single cone is fully opaque, and
        // at lower master values extra cones (coverage > 1) still read as denser.
        float coverageAlpha = baseAlpha + (total - 1) * stepAlpha;
        float alpha = Mathf.Min(maxAlpha, coverageAlpha * globalOpacity);

        // The palette is authored in sRGB; vertex colours go straight to the linear target.
        Color source = lethal ? lethalColor : warningColor;
        Color color = source.linear;
        color.a = Mathf.Clamp01(alpha * source.a);
        Color edgeColor = color;
        edgeColor.a = 0f;

        // Solid core: a centre vertex plus an inner ring, all at full alpha. The outer ring at
        // zero alpha forms the soft rim. Because the core is geometry rather than a texture,
        // it stays fully opaque at every dot size.
        //
        // Every shape shares dotRadius as its circumradius: the circle keeps its 16-sided
        // outline, while the diamond and square use their four vertices on that same circle.
        int segments = dotShape == VisionOverlayShape.Circle ? CircleSegments : PolygonSegments;

        int centerIndex = vertices.Count;
        vertices.Add(tintTransform.InverseTransformPoint(worldCenter));
        colors.Add(color);

        int ringStart = vertices.Count;
        for (int i = 0; i < segments; i++)
        {
            float angle = ShapeVertexAngle(i, segments);
            Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);

            Vector3 inner = worldCenter + direction * (dotRadius * DotSolidFraction);
            Vector3 outer = worldCenter + direction * dotRadius;

            vertices.Add(tintTransform.InverseTransformPoint(inner));
            colors.Add(color);
            vertices.Add(tintTransform.InverseTransformPoint(outer));
            colors.Add(edgeColor);
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int inner = ringStart + i * 2;
            int outer = inner + 1;
            int innerNext = ringStart + next * 2;
            int outerNext = innerNext + 1;

            // Solid core.
            triangles.Add(centerIndex);
            triangles.Add(inner);
            triangles.Add(innerNext);

            // Fading rim.
            triangles.Add(inner);
            triangles.Add(outer);
            triangles.Add(outerNext);
            triangles.Add(inner);
            triangles.Add(outerNext);
            triangles.Add(innerNext);
        }
    }

    private float ShapeVertexAngle(int index, int segments)
    {
        if (dotShape == VisionOverlayShape.Circle)
            return (Mathf.PI * 2f * index) / segments;

        // Four-vertex shapes: Diamond has its points on the axes, Square has its corners
        // halfway between them. Both stay on the same dotRadius circle.
        float quarterTurn = (Mathf.PI * 0.5f) * index;
        return dotShape == VisionOverlayShape.Diamond
            ? quarterTurn
            : quarterTurn + Mathf.PI * 0.25f;
    }
}
