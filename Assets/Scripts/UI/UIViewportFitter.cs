using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Pins a Screen Space - Overlay canvas to the rectangle the game actually renders into, so menus
// never spill onto the letterbox/pillarbox bars and always grow and shrink with the game instead of
// with the raw screen.
//
// Screen and the canvas are not the same thing here. PersistentCameraController renders the world at
// a fixed reference resolution and the Pixel Perfect Camera blits that image into an integer-scaled,
// centred viewport (Camera.pixelRect), with black bars taking the remainder. CanvasScaler only ever
// looks at Screen.width/height, so a ScaleWithScreenSize canvas happily lays itself out over those
// bars - which is exactly what every menu was doing.
//
// This component reads the two numbers the camera already worked out instead:
//   Camera.pixelRect  - the bar-excluded rectangle the game image is blitted into
//   pixelRatio        - the whole-number sprite multiplier the camera chose ("Current Pixel Ratio")
//
// Deriving the UI scale from pixelRatio rather than from the screen is what keeps the two locked
// together: one design unit is worth exactly 1 / designUnitsPerArtPixel art pixels, so the UI's own
// pixels sit on the game's pixel grid and a panel authored at a multiple of designUnitsPerArtPixel
// lands on whole art pixels at every zoom.
//
// The design space is the game's visible area itself, multiplied by designUnitsPerArtPixel, so the
// scale below always lands on the art-pixel grid and the UI fills the frame exactly whatever shape
// the frame is. The pause container is authored at 1720x880 units, which is 430x220 art pixels, so
// it fits any frame up to and including a 430x242 one; a frame shorter than 220 art pixels makes the
// panel spill past the visible area rather than shrink, which is the signal that the panel needs
// re-fitting for that shape.
//
// DesignSpace.Pinned is the alternative: hold the design space at designResolution for ever so the
// layout numbers never move, in exchange for a fractional (soft) scale whenever the frame's shape
// stops matching it.
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(CanvasScaler))]
public sealed class UIViewportFitter : MonoBehaviour
{
    private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

    public enum DesignSpace
    {
        // Design space is the game's visible area, so the grid scale always fits: the UI rides the
        // game's pixels and fills the frame at any shape. A panel bigger than the visible area
        // spills past it instead of shrinking.
        MatchFrame,

        // Design space is fixed at designResolution, so layout numbers hold still when the frame
        // changes. The scale is capped, so a frame too small for it shrinks the panel rather than
        // letting it spill, at the cost of pixel alignment.
        Pinned
    }

    [Header("Fit")]
    [Tooltip("Content root driven to the game's visible area. Defaults to this canvas's first child.")]
    [SerializeField] private RectTransform frame;

    [Tooltip("Safe-area reference for DesignSpace.Pinned: the art-pixel area the UI is laid out " +
             "against when the design space is not following the frame. Ignored by MatchFrame.")]
    [SerializeField] private Vector2Int designResolution = new Vector2Int(430, 242);

    [Tooltip("How many layout units equal one art pixel. 4 is what the current menu layouts use; " +
             "set it to 1 to author straight in art pixels.")]
    [SerializeField, Min(1)] private int designUnitsPerArtPixel = 4;

    [Tooltip("MatchFrame lays the UI out in the game's own visible area, so it stays on the pixel " +
             "grid and fills the frame at any shape. Pinned holds the design space at " +
             "designResolution instead, which keeps layout numbers fixed but goes soft whenever the " +
             "frame's shape differs.")]
    [SerializeField] private DesignSpace designSpace = DesignSpace.MatchFrame;

    [Header("Editor")]
    [Tooltip("Refit while not playing, so panels can be tuned without entering play mode.")]
    [SerializeField] private bool previewInEditor = true;

    [Tooltip("Log the fitted viewport and scale whenever they change.")]
    [SerializeField] private bool logChanges;

    // Readouts for other UI code. ViewportRect is in screen pixels with the same bottom-left origin
    // Camera.pixelRect uses; ScaleFactor is screen pixels per layout unit.
    public Rect ViewportRect { get; private set; }
    public int PixelRatio { get; private set; } = 1;
    public float ScaleFactor { get; private set; } = 1f;

    // The design space actually in use: the frame under MatchFrame, DesignSize under Pinned.
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
    private Camera gameCamera;
    private PixelPerfectCamera pixelPerfect;

    // Change guards. Everything here is re-derived every frame and a write that changes nothing
    // would still dirty the scene in edit mode, so each value is compared before it is assigned.
    private Rect lastViewport;
    private float lastScaleFactor = -1f;

    private void Awake() => Resolve();

    private void OnEnable() => Apply();

    private void OnValidate() => Resolve();

    // LateUpdate, not Update: the Pixel Perfect Camera derives pixelRect from its begin-camera
    // callback, so the value sitting on the camera at the end of the frame is the one that was
    // actually rendered, and the one the UI should agree with.
    private void LateUpdate() => Apply();

    private void Resolve()
    {
        if (canvas == null)
            canvas = GetComponent<Canvas>();

        if (scaler == null)
            scaler = GetComponent<CanvasScaler>();

        if (frame == null && transform.childCount > 0)
            frame = transform.GetChild(0) as RectTransform;

        // The Pixel Perfect Camera has to sit on the camera it adjusts, so finding the rendering
        // camera finds the component that owns the viewport. Re-resolved while the cached pair is
        // missing, which is what happens after a scene load swaps the camera out.
        if (pixelPerfect == null || gameCamera == null)
        {
            Camera main = Camera.main;
            if (main != null)
            {
                gameCamera = main;
                pixelPerfect = main.GetComponent<PixelPerfectCamera>();
            }
            else
            {
                pixelPerfect = FindAnyObjectByType<PixelPerfectCamera>();
                gameCamera = pixelPerfect != null ? pixelPerfect.GetComponent<Camera>() : null;
            }
        }
    }

    private void Apply()
    {
        Resolve();

        if (scaler == null)
            return;

        if (!Application.isPlaying && !previewInEditor)
            return;

        if (!TryGetViewport(out Rect viewport, out int pixelRatio))
            return;

        int grid = Mathf.Max(1, designUnitsPerArtPixel);

        // One design unit is one grid-th of an art pixel, so this is the only scale that keeps the
        // UI sitting on the game's pixel grid.
        float artPixelScale = pixelRatio / (float)grid;

        // MatchFrame takes the design space from the frame, so the grid scale fits by construction
        // and the cap below never engages. Pinned keeps the authored space, which a small frame may
        // not accommodate; the fit ratio then wins and the panel shrinks instead of spilling onto
        // the bars, at the cost of landing off the pixel grid.
        Vector2 designSize = designSpace == DesignSpace.MatchFrame
            ? new Vector2(viewport.width, viewport.height) / artPixelScale
            : DesignSize;

        float fitScale = Mathf.Min(viewport.width / designSize.x, viewport.height / designSize.y);
        float scaleFactor = Mathf.Min(artPixelScale, fitScale);

        if (scaleFactor <= 0f)
            return;

        // ConstantPixelSize turns CanvasScaler into a plain "canvas.scaleFactor = m_ScaleFactor"
        // pass-through, which stops it re-deriving a screen-based factor behind us.
        if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ConstantPixelSize)
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        if (!Mathf.Approximately(scaler.scaleFactor, scaleFactor))
            scaler.scaleFactor = scaleFactor;

        if (canvas != null && !Mathf.Approximately(canvas.scaleFactor, scaleFactor))
            canvas.scaleFactor = scaleFactor;

        ViewportRect = viewport;
        PixelRatio = pixelRatio;
        ScaleFactor = scaleFactor;
        ActiveDesignSize = designSize;

        if (frame != null)
            ApplyFrame(viewport, scaleFactor);

        if (logChanges && (lastScaleFactor < 0f || lastViewport != viewport || !Mathf.Approximately(lastScaleFactor, scaleFactor)))
            Debug.Log($"[UIViewportFitter] {name}: viewport={viewport} ratio={pixelRatio} scale={scaleFactor:F5} frame={frame.sizeDelta}", this);

        lastViewport = viewport;
        lastScaleFactor = scaleFactor;
    }

    // The frame is the viewport expressed in design units and centred on the viewport's centre rather
    // than the screen's. With matching aspects this is exactly DesignSize; deriving it from the
    // viewport keeps it right when the frame is not the shape the UI was authored for.
    private void ApplyFrame(Rect viewport, float scaleFactor)
    {
        Vector2 frameSize = new Vector2(viewport.width, viewport.height) / scaleFactor;
        Vector2 screenCentre = new Vector2(Screen.width, Screen.height) * 0.5f;
        Vector2 framePosition = (viewport.center - screenCentre) / scaleFactor;

        // Centre anchors make anchoredPosition a plain offset from the frame's own middle, and the
        // children that are already centre-anchored keep their relative placement.
        if (frame.anchorMin != Centre || frame.anchorMax != Centre || frame.pivot != Centre)
        {
            frame.anchorMin = Centre;
            frame.anchorMax = Centre;
            frame.pivot = Centre;
        }

        if (Vector2.Distance(frame.sizeDelta, frameSize) > 0.01f)
            frame.sizeDelta = frameSize;

        if (Vector2.Distance(frame.anchoredPosition, framePosition) > 0.01f)
            frame.anchoredPosition = framePosition;
    }

    // The game's visible rectangle in screen pixels. Without a Pixel Perfect Camera there is no
    // viewport to speak of, so the whole screen is used and the UI behaves the way it always did.
    private bool TryGetViewport(out Rect viewport, out int pixelRatio)
    {
        viewport = new Rect(0f, 0f, Screen.width, Screen.height);
        pixelRatio = 1;

        if (pixelPerfect != null && gameCamera != null)
        {
            Rect cameraRect = gameCamera.pixelRect;

            // Before the camera has rendered once there is nothing meaningful to fit to; the next
            // frame picks it up. A degenerate rect would otherwise collapse the whole canvas.
            if (cameraRect.width < 1f || cameraRect.height < 1f)
                return false;

            viewport = cameraRect;
            pixelRatio = Mathf.Max(1, pixelPerfect.pixelRatio);
        }

        return viewport.width >= 1f && viewport.height >= 1f;
    }
}
