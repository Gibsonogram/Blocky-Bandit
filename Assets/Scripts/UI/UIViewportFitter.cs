using UnityEngine;
using UnityEngine.UI;

// Pins a Screen Space - Overlay canvas to a fixed 3:2 box in the middle of the screen, at the largest
// even whole-number art scale that fits, and keeps it there whatever the camera is doing.
//
// The UI is its own layer. A Screen Space - Overlay canvas is composited at native screen resolution
// and the Pixel Perfect Camera never touches it: its crop rect, its upscale blit and its ortho
// correction only ever affect cameras. The one thing that used to tie the UI to the camera was this
// component asking Camera.pixelRect how big to be, so the menus resized (and landed on fractional
// scales) whenever the camera frame changed between gameplay and the world map.
//
// Size therefore comes from the screen:
//   uiZoom      - the largest even whole number of screen pixels per UI art pixel that still fits
//                 designResolution on the screen: 4 at 1920x1080, 8 at 3840x2160, 2 at 1600x900.
//                 Even, because pixel art reads best at an even multiple, and because it keeps the
//                 UI's own art on whole screen pixels at every size. A window too small for any even
//                 multiple uses the largest whole number that fits, so the UI can never spill off.
//   scaleFactor - uiZoom / designUnitsPerArtPixel. One art pixel is exactly uiZoom screen pixels, so
//                 panel edges and glyphs land on whole pixels instead of being scaled by a fraction.
//   box         - designResolution * uiZoom screen pixels: always 3:2, always centred. On a wider
//                 screen the extra space simply shows more world around it.
//
// Because the formula only reads the screen, the box is identical in gameplay, on the world map and
// in the pause screen - a mode transition cannot move or resize the UI. It also lines up with the
// gameplay frame wherever the screen allows it, because the Pixel Perfect Camera picks its zoom from
// the same arithmetic.
//
// frame is a bare container this component sizes and centres in design units; panels are authored
// inside it at the size they should appear (a full-screen screen is 1536x1024 units, an inset one is
// smaller). No other transform in the scene is written to.
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(CanvasScaler))]
public sealed class UIViewportFitter : MonoBehaviour
{
    private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

    [Header("Fit")]
    [Tooltip("The bare container this component sizes and centres. It must not be a screen's panel: " +
             "screens are authored inside it at their own design size and are never resized here.")]
    [SerializeField] private RectTransform frame;

    [Tooltip("The UI's design box in art pixels. The scale is the largest even whole multiple of this " +
             "that fits the screen, so the box keeps this aspect (384x256 = 3:2) at every resolution.")]
    [SerializeField] private Vector2Int designResolution = new Vector2Int(384, 256);

    [Tooltip("How many layout units equal one art pixel. 4 is what the current menu layouts use; set " +
             "it to 1 to author straight in art pixels.")]
    [SerializeField, Min(1)] private int designUnitsPerArtPixel = 4;

    [Header("Editor")]
    [Tooltip("Refit while not playing, so panels can be tuned without entering play mode.")]
    [SerializeField] private bool previewInEditor = true;

    [Tooltip("Log the fitted box and scale whenever they change.")]
    [SerializeField] private bool logChanges;

    // Readouts. UiRect is the 3:2 box in screen pixels with the same bottom-left origin Screen uses.
    // ScaleFactor is screen pixels per layout unit; PixelRatio is screen pixels per UI art pixel.
    public Rect UiRect { get; private set; }
    public int PixelRatio { get; private set; } = 1;
    public float ScaleFactor { get; private set; } = 1f;
    public Vector2 ActiveDesignSize { get; private set; }

    public Vector2 DesignSize
    {
        get
        {
            int grid = Mathf.Max(1, designUnitsPerArtPixel);
            return new Vector2(Mathf.Max(1, designResolution.x) * grid, Mathf.Max(1, designResolution.y) * grid);
        }
    }

    private Canvas canvas;
    private CanvasScaler scaler;

    // Change guards. Everything here is re-derived every frame and a write that changes nothing would
    // still dirty the scene in edit mode, so each value is compared before it is assigned.
    private Rect lastUiRect;
    private float lastScaleFactor = -1f;

    private void Awake() => Resolve();

    private void OnEnable() => Apply();

    private void OnValidate() => Resolve();

    // LateUpdate, so a resolution change or a layout rebuild settles before the canvas is drawn.
    private void LateUpdate() => Apply();

    private void Resolve()
    {
        if (canvas == null)
            canvas = GetComponent<Canvas>();

        if (scaler == null)
            scaler = GetComponent<CanvasScaler>();

        if (frame == null && transform.childCount > 0)
            frame = transform.GetChild(0) as RectTransform;
    }

    private void Apply()
    {
        Resolve();

        if (scaler == null)
            return;

        if (!Application.isPlaying && !previewInEditor)
            return;

        if (Screen.width < 2 || Screen.height < 2)
            return;

        int grid = Mathf.Max(1, designUnitsPerArtPixel);
        Vector2Int reference = new Vector2Int(Mathf.Max(1, designResolution.x), Mathf.Max(1, designResolution.y));

        int uiZoom = LargestEvenMultipleThatFits(reference);
        float scaleFactor = uiZoom / (float)grid;
        if (scaleFactor <= 0f)
            return;

        Vector2 boxSize = new Vector2(reference.x * uiZoom, reference.y * uiZoom);

        // ConstantPixelSize turns CanvasScaler into a plain "canvas.scaleFactor = m_ScaleFactor"
        // pass-through, which stops it deriving a continuous screen-based factor behind us.
        if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ConstantPixelSize)
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        if (!Mathf.Approximately(scaler.scaleFactor, scaleFactor))
            scaler.scaleFactor = scaleFactor;

        if (canvas != null && !Mathf.Approximately(canvas.scaleFactor, scaleFactor))
            canvas.scaleFactor = scaleFactor;

        if (frame != null)
            ApplyFrame(boxSize, scaleFactor);

        UiRect = new Rect((Screen.width - boxSize.x) * 0.5f, (Screen.height - boxSize.y) * 0.5f,
                          boxSize.x, boxSize.y);
        PixelRatio = uiZoom;
        ScaleFactor = scaleFactor;
        ActiveDesignSize = DesignSize;

        if (logChanges && (lastScaleFactor < 0f || lastUiRect != UiRect || !Mathf.Approximately(lastScaleFactor, scaleFactor)))
            Debug.Log($"[UIViewportFitter] {name}: screen={Screen.width}x{Screen.height} box={UiRect} " +
                      $"zoom={uiZoom} scale={scaleFactor:F3}", this);

        lastUiRect = UiRect;
        lastScaleFactor = scaleFactor;
    }

    // Largest even whole number of screen pixels per art pixel that still fits the design resolution.
    // Even multiples (2, 4, 6, 8 ...) keep UI art on a consistent whole-pixel grid; a window too small
    // for one falls back to the largest whole number that fits rather than spilling off screen.
    private static int LargestEvenMultipleThatFits(Vector2Int reference)
    {
        float fit = Mathf.Min(Screen.width / (float)reference.x, Screen.height / (float)reference.y);
        int largest = Mathf.Max(1, Mathf.FloorToInt(fit));
        int even = largest - (largest % 2);
        return even >= 2 ? even : largest;
    }

    // The container is a constant size in design units and always centred. boxSize / scaleFactor is
    // designResolution * designUnitsPerArtPixel, so this maps back to exactly the box in pixels.
    private void ApplyFrame(Vector2 boxSize, float scaleFactor)
    {
        Vector2 frameSize = boxSize / scaleFactor;

        if (frame.anchorMin != Centre || frame.anchorMax != Centre || frame.pivot != Centre)
        {
            frame.anchorMin = Centre;
            frame.anchorMax = Centre;
            frame.pivot = Centre;
        }

        if (Vector2.Distance(frame.sizeDelta, frameSize) > 0.01f)
            frame.sizeDelta = frameSize;

        if (Vector2.Distance(frame.anchoredPosition, Vector2.zero) > 0.01f)
            frame.anchoredPosition = Vector2.zero;
    }
}
