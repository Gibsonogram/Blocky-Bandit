using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// One camera rig for the whole game. It lives in the Persistent scene so there is a single place
// to tune the view; level scenes only contribute a PlayerController and a "CameraConfiner".
//
// The pixel grid and the visible area are both derived from the serialized fields here, because
// the Pixel Perfect Camera and Cinemachine both drive the same orthographic size and fight:
//   - Cinemachine copies CinemachineCamera.Lens onto the Camera every frame.
//   - PixelPerfectCamera rewrites that size to whole-zoom multiples (CorrectCinemachineOrthoSize)
//     and, from that point on, stops writing Camera.orthographicSize itself.
// Setting only one of them is why the old rig looked unchangeable: the value typed into the lens
// was quietly divided by an integer zoom. This component pushes one consistent set of values into
// both, from the two serialized tile counts.
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
[RequireComponent(typeof(CinemachineConfiner2D))]
public sealed class PersistentCameraController : MonoBehaviour
{
    public enum ViewFit
    {
        // Whole-number pixel zoom with black bars filling the rest of the screen. The tile count is
        // identical on every device and no tile can land on a fractional pixel, so tile seams stay
        // clean. This is the safe default for a pixel-art game shipping to mixed hardware.
        FixedFrame,

        // Fills the screen edge to edge. The tile count then follows the screen's aspect ratio, and
        // the pixel ratio can land on a fraction, which is what produces broken tile edges.
        FillScreen
    }

    private const string PersistentSceneName = "Persistent";
    private const string CameraConfinerName = "CameraConfiner";

    [Header("Pixel Grid")]
    [Tooltip("Must match the Pixels Per Unit of the tile sprites. 32 keeps 32x32 tiles on whole pixels.")]
    [SerializeField] private int pixelsPerUnit = 32;

    [Header("Visible Area")]
    [Tooltip("How many tiles fit horizontally. Width and height are independent so the frame can be " +
             "any shape. Art pixels stay square whatever you pick: the Pixel Perfect Camera scales " +
             "both axes by the same whole-number zoom, so this only changes how much world is " +
             "visible. Fractional values are allowed and round to the nearest art pixel, so 13.4375 " +
             "gives exactly 430 pixels.")]
    [SerializeField] private float visibleTilesHorizontal = 13.4375f;

    [Tooltip("How many tiles fit vertically. 7.5625 gives exactly 242 pixels. 16:9 is the pair " +
             "13.4375 x 7.5625 (a 430x242 frame) or 10 x 5.625 (320x180); 4:3 is 10.6667 x 8; " +
             "16:10 is 12.1 x 7.5625.")]
    [SerializeField] private float visibleTilesVertical = 7.5625f;

    [Tooltip("FixedFrame keeps the same view on every device with bars filling the rest; FillScreen " +
             "follows the screen's aspect and lets the tile count float.")]
    [SerializeField] private ViewFit fit = ViewFit.FixedFrame;

    // Derived readouts. These are what the rest of the rig is built from, and what to watch while
    // tuning the two knobs above: they are whole art pixels by construction.
    public int FramePixelWidth => Mathf.Max(2, Mathf.RoundToInt(Mathf.Max(0.05f, visibleTilesHorizontal) * PixelsPerUnit));
    public int FramePixelHeight => Mathf.Max(2, Mathf.RoundToInt(Mathf.Max(0.05f, visibleTilesVertical) * PixelsPerUnit));
    public int PixelsPerUnit => Mathf.Max(1, pixelsPerUnit);

    // The tile counts actually achieved after rounding to whole pixels. These differ from the knobs
    // only by the sub-pixel remainder, which is unavoidable: a pixel cannot be split.
    public float VisibleTilesHorizontal => FramePixelWidth / (float)PixelsPerUnit;
    public float VisibleTilesVertical => FramePixelHeight / (float)PixelsPerUnit;

    // The shape that actually gets rendered. Rounding nudges this away from the ratio of the two
    // knobs by a fraction of a pixel, so read it back rather than assuming the knobs divide cleanly.
    public float FrameAspect => FramePixelWidth / (float)FramePixelHeight;

    private CinemachineCamera cinemachineCamera;
    private CinemachineConfiner2D confiner;
    private PixelPerfectCamera pixelPerfectCamera;
    private Coroutine bindRoutine;

    private void Awake()
    {
        cinemachineCamera = GetComponent<CinemachineCamera>();
        confiner = GetComponent<CinemachineConfiner2D>();
    }

    private void OnEnable()
    {
        // ExecuteAlways: keep the authored rig matching these fields in the editor too, so tuning
        // a value is visible immediately instead of only after entering play mode.
        ApplyViewSettings();

        if (!Application.isPlaying)
            return;

        SceneManager.sceneLoaded += OnSceneLoaded;
        BindToActiveLevel();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (bindRoutine != null)
        {
            StopCoroutine(bindRoutine);
            bindRoutine = null;
        }
    }

    private void OnValidate()
    {
        if (cinemachineCamera == null)
            cinemachineCamera = GetComponent<CinemachineCamera>();
        if (confiner == null)
            confiner = GetComponent<CinemachineConfiner2D>();

        ApplyViewSettings();
    }

    // Single source of truth for the view. Idempotent, so it is safe to call whenever a value
    // changes; the comparisons keep it from dirtying the scene with writes that change nothing.
    public void ApplyViewSettings()
    {
        if (cinemachineCamera == null)
            return;

        int ppu = PixelsPerUnit;
        int frameHeight = FramePixelHeight;
        int frameWidth = FramePixelWidth;

        // Half the frame height in world units. Because the frame is a whole number of art pixels,
        // this is exactly the size PixelPerfectCamera computes internally, so its zoom correction
        // stays at 1x and never divides the value we ask for.
        float targetOrthoSize = frameHeight * 0.5f / ppu;

        LensSettings lens = cinemachineCamera.Lens;
        if (lens.ModeOverride != LensSettings.OverrideModes.Orthographic ||
            !Mathf.Approximately(lens.OrthographicSize, targetOrthoSize))
        {
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = targetOrthoSize;
            cinemachineCamera.Lens = lens;
        }

        PixelPerfectCamera pixelPerfect = ResolvePixelPerfectCamera();
        if (pixelPerfect != null)
        {
            bool fixedFrame = fit == ViewFit.FixedFrame;

            if (pixelPerfect.assetsPPU != ppu)
                pixelPerfect.assetsPPU = ppu;
            if (pixelPerfect.refResolutionX != frameWidth)
                pixelPerfect.refResolutionX = frameWidth;
            if (pixelPerfect.refResolutionY != frameHeight)
                pixelPerfect.refResolutionY = frameHeight;

            PixelPerfectCamera.CropFrame cropFrame = fixedFrame
                ? PixelPerfectCamera.CropFrame.Windowbox
                : PixelPerfectCamera.CropFrame.None;
            if (pixelPerfect.cropFrame != cropFrame)
                pixelPerfect.cropFrame = cropFrame;

            PixelPerfectCamera.GridSnapping gridSnapping = fixedFrame
                ? PixelPerfectCamera.GridSnapping.UpscaleRenderTexture
                : PixelPerfectCamera.GridSnapping.PixelSnapping;
            if (pixelPerfect.gridSnapping != gridSnapping)
                pixelPerfect.gridSnapping = gridSnapping;
        }

        // The confiner caches its solution per lens size, so a view change invalidates it.
        if (confiner != null && confiner.BoundingShape2D != null)
            confiner.InvalidateBoundingShapeCache();
    }

    private PixelPerfectCamera ResolvePixelPerfectCamera()
    {
        if (pixelPerfectCamera != null)
            return pixelPerfectCamera;

        CinemachineBrain brain = cinemachineCamera != null
            ? CinemachineCore.FindPotentialTargetBrain(cinemachineCamera)
            : null;

        Camera outputCamera = brain != null ? brain.OutputCamera : null;
        if (outputCamera == null)
            outputCamera = Camera.main;

        if (outputCamera != null)
            pixelPerfectCamera = outputCamera.GetComponent<PixelPerfectCamera>();

        // Before the brain has resolved its output camera (fresh load, or edit mode).
        if (pixelPerfectCamera == null)
            pixelPerfectCamera = FindAnyObjectByType<PixelPerfectCamera>();

        return pixelPerfectCamera;
    }

    private void OnSceneLoaded(Scene loadedScene, LoadSceneMode loadSceneMode)
    {
        if (loadedScene.name == PersistentSceneName)
            return;

        if (bindRoutine != null)
            StopCoroutine(bindRoutine);

        bindRoutine = StartCoroutine(BindAfterSceneInitialization(loadedScene));
    }

    private IEnumerator BindAfterSceneInitialization(Scene loadedScene)
    {
        yield return null;
        bindRoutine = null;

        if (loadedScene.isLoaded)
            BindToScene(loadedScene);
    }

    private void BindToActiveLevel()
    {
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (scene.isLoaded && scene.name != PersistentSceneName)
            {
                BindToScene(scene);
                return;
            }
        }
    }

    private void BindToScene(Scene scene)
    {
        // Apply before binding: the confiner solves its caches against the current lens size.
        ApplyViewSettings();

        PlayerController player = FindComponentInScene<PlayerController>(scene);
        if (player == null)
        {
            Debug.LogWarning($"Persistent camera could not find a {nameof(PlayerController)} in scene '{scene.name}'.", this);
            return;
        }

        cinemachineCamera.Follow = player.transform;
        cinemachineCamera.LookAt = player.transform;

        // A missing confiner means "unconfined", not "keep the last level's bounds": the world map
        // has no CameraConfiner, and a stale shape would clamp the camera to geometry it has not got.
        Collider2D boundingShape = FindConfinerInScene(scene);
        confiner.BoundingShape2D = boundingShape;
        confiner.InvalidateBoundingShapeCache();
        cinemachineCamera.CancelDamping(true);
    }

    private static Collider2D FindConfinerInScene(Scene scene)
    {
        GameObject[] rootObjects = scene.GetRootGameObjects();
        foreach (GameObject rootObject in rootObjects)
        {
            Transform[] transforms = rootObject.GetComponentsInChildren<Transform>(true);
            foreach (Transform transform in transforms)
            {
                if (transform.name == CameraConfinerName && transform.TryGetComponent(out Collider2D collider))
                    return collider;
            }
        }

        return null;
    }

    private static T FindComponentInScene<T>(Scene scene) where T : Component
    {
        GameObject[] rootObjects = scene.GetRootGameObjects();
        foreach (GameObject rootObject in rootObjects)
        {
            T component = rootObject.GetComponentInChildren<T>(true);
            if (component != null)
                return component;
        }

        return null;
    }
}
