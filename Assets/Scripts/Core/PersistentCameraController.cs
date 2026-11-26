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
//
// Those two tile counts are the gameplay frame (12 x 8 = 384x256 art pixels). A scene can ask for a
// different frame with a CameraFrameOverride: the world map does, and asks for 24 x 16 tiles
// (768x512 art pixels). Widening the frame is what zooms out - the tiles stay 32x32 texels at 1:1
// with the art pixels, and the Pixel Perfect Camera simply upscales the whole picture by 2 instead of
// 4. (Zooming the camera itself would divide those texels - that is the one thing to never do here.)
// The rig falls back to these serialized values for every scene that does not declare an override,
// and a scene can also ask for a fixed focus or a snapping camera through the same component.
//
// The three presentations share one number, the whole-number art scale gameplay renders at:
//   - gameplay (FixedFrame) renders at the largest whole-number zoom that fits the frame, odd or even:
//     4 at 1920x1080, 3 at 1600x900, 2 at 1280x720. Discreteness is what keeps art pixels whole.
//   - the world map (FillScreen) renders at half of the largest *even* gameplay zoom, because its own
//     frame is exactly twice as wide: gameplay at 3x gives a 1x map, gameplay at 4x or 5x gives 2x.
//     Only the even half is used so the halved scale stays a whole number.
//   - the UI (UIViewportFitter, a Screen Space - Overlay canvas) uses gameplay's zoom unchanged, odd
//     values included, so the menus are the same size on screen in every mode.
// GameplayPixelScale is that shared number. The rig exposes it so the UI follows gameplay instead of
// re-deriving it and drifting by a step.
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
        // clean. This is the gameplay presentation: 12 x 8 tiles at the largest whole-number zoom that
        // fits, windowboxed.
        //
        // If the window is too small to hold the frame, the view falls back to the window at 1:1
        // (crisp, slightly less world) rather than rescaling the art by a fraction.
        FixedFrame,

        // Fills the screen edge to edge with world instead of bars. The window becomes the reference
        // resolution and the camera's ortho size sets the art scale, so pixels are still exactly a
        // whole number of screen pixels wide - just more of the world is visible, and how much follows
        // the screen's aspect. The frame's tile counts act as a minimum rather than an exact count.
        // The art scale is half of the largest even gameplay zoom, which is what keeps 32x32 tiles at
        // 1:1 with art pixels while twice the world fits on screen. This is the world map's
        // presentation.
        FillScreen
    }

    private const string PersistentSceneName = "Persistent";
    private const string CameraConfinerName = "CameraConfiner";

    [Header("Pixel Grid")]
    [Tooltip("Must match the Pixels Per Unit of the tile sprites. 32 keeps 32x32 tiles on whole pixels.")]
    [SerializeField] private int pixelsPerUnit = 32;

    [Header("Visible Area")]
    [Tooltip("How many tiles fit horizontally in the gameplay frame. Width and height are " +
             "independent so the frame can be any shape. Art pixels stay square whatever you pick: " +
             "the Pixel Perfect Camera scales both axes by the same whole-number zoom, so this only " +
             "changes how much world is visible. 12 is the gameplay frame; the world map overrides " +
             "this to 24 with a CameraFrameOverride in its scene.")]
    [SerializeField] private float visibleTilesHorizontal = 12f;

    [Tooltip("How many tiles fit vertically in the gameplay frame. 8 pairs with 12 for the 384x256 " +
             "frame (3:2) that gameplay and the title screen use. Fractional values are allowed and " +
             "round to the nearest art pixel, so 7.5625 gives exactly 242 pixels.")]
    [SerializeField] private float visibleTilesVertical = 8f;

    [Tooltip("FixedFrame keeps the same view on every device with bars filling the rest; FillScreen " +
             "follows the screen's aspect and lets the tile count float.")]
    [SerializeField] private ViewFit fit = ViewFit.FixedFrame;

    // Derived readouts. These are what the rest of the rig is built from, and what to watch while
    // tuning the two knobs above: they are whole art pixels by construction.
    public int FramePixelWidth => TileCountToPixels(ActiveTilesHorizontal);
    public int FramePixelHeight => TileCountToPixels(ActiveTilesVertical);
    public int PixelsPerUnit => Mathf.Max(1, pixelsPerUnit);

    // The gameplay frame specifically: the serialized tile counts, with any scene override ignored.
    // The world map and the UI both measure themselves against this frame rather than the loaded
    // scene's, so neither of them changes size when the map widens the view.
    private int GameplayFramePixelWidth => TileCountToPixels(visibleTilesHorizontal);
    private int GameplayFramePixelHeight => TileCountToPixels(visibleTilesVertical);

    // The whole-number art scale gameplay is rendered at: the Pixel Perfect Camera's own zoom, which is
    // the largest number of screen pixels per art pixel that fits the gameplay frame - odd or even
    // (4 at 1920x1080, 3 at 1600x900, 2 at 1280x720). The UI sizes itself from this so the menus are
    // exactly as many screen pixels per art pixel as gameplay, in every mode.
    public int GameplayPixelScale
    {
        get
        {
            float fit = Mathf.Min(Mathf.Max(1, Screen.width) / (float)GameplayFramePixelWidth,
                                  Mathf.Max(1, Screen.height) / (float)GameplayFramePixelHeight);
            return Mathf.Max(1, Mathf.FloorToInt(fit));
        }
    }

    // A tile count as whole art pixels. 0.05 is the floor the inspector allows, and a frame cannot be
    // narrower than a couple of pixels or the Pixel Perfect Camera has nothing to scale.
    private int TileCountToPixels(float tiles) =>
        Mathf.Max(2, Mathf.RoundToInt(Mathf.Max(0.05f, tiles) * PixelsPerUnit));

    // The tile counts actually achieved after rounding to whole pixels. These differ from the knobs
    // only by the sub-pixel remainder, which is unavoidable: a pixel cannot be split.
    public float VisibleTilesHorizontal => FramePixelWidth / (float)PixelsPerUnit;
    public float VisibleTilesVertical => FramePixelHeight / (float)PixelsPerUnit;

    // The shape that actually gets rendered. Rounding nudges this away from the ratio of the two
    // knobs by a fraction of a pixel, so read it back rather than assuming the knobs divide cleanly.
    public float FrameAspect => FramePixelWidth / (float)FramePixelHeight;

    // The one rig in the session. UI code asks it for the rendering camera instead of guessing
    // Camera.main, which was ambiguous while scenes shipped camera rigs of their own.
    public static PersistentCameraController Instance { get; private set; }

    public Camera OutputCamera
    {
        get
        {
            if (outputCamera == null)
                outputCamera = ResolveOutputCamera();

            return outputCamera;
        }
    }

    // A scene can replace the gameplay frame for as long as it is the loaded scene. The world map
    // does this (24 x 16 tiles, filled rather than letterboxed); everything else inherits the
    // serialized frame above, because the override is cleared the next time a scene binds.
    private bool hasSceneFrame;
    private float sceneTilesHorizontal;
    private float sceneTilesVertical;
    private bool sceneFillScreen;

    private float ActiveTilesHorizontal => hasSceneFrame ? sceneTilesHorizontal : visibleTilesHorizontal;
    private float ActiveTilesVertical => hasSceneFrame ? sceneTilesVertical : visibleTilesVertical;

    private CinemachineCamera cinemachineCamera;
    private CinemachineConfiner2D confiner;
    private CinemachinePositionComposer positionComposer;
    private PixelPerfectCamera pixelPerfectCamera;
    private Camera outputCamera;
    private Coroutine bindRoutine;

    // The damping authored on the rig, restored whenever a scene does not ask for something else.
    private Vector3 authoredDamping = Vector3.zero;

    // Last window size the view was applied for, so a resize can re-evaluate whether the frame fits.
    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private string lastFallbackLog;

    private void Awake()
    {
        cinemachineCamera = GetComponent<CinemachineCamera>();
        confiner = GetComponent<CinemachineConfiner2D>();
        positionComposer = GetComponent<CinemachinePositionComposer>();
        if (positionComposer != null)
            authoredDamping = positionComposer.Damping;
    }

    private void OnEnable()
    {
        Instance = this;

        if (!Application.isPlaying)
        {
            // Edit mode: keep the authored rig matching these fields, and honour a frame declared by
            // another open scene. That scene's override may have enabled before this rig existed, so
            // look one up rather than assuming the gameplay frame.
            ApplySceneFrame(FindLoadedSceneFrameOverride());
            return;
        }

        ApplyViewSettings();
        SceneManager.sceneLoaded += OnSceneLoaded;
        BindToActiveLevel();
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;

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

    // A window resize changes whether the frame fits, and therefore which presentation is correct.
    private void Update()
    {
        if (!Application.isPlaying)
            return;

        if (Screen.width == lastScreenWidth && Screen.height == lastScreenHeight)
            return;

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        ApplyViewSettings();
    }

    // Single source of truth for the view. Idempotent, so it is safe to call whenever a value
    // changes; the comparisons keep it from dirtying the scene with writes that change nothing.
    public void ApplyViewSettings()
    {
        if (cinemachineCamera == null)
            return;

        int ppu = PixelsPerUnit;
        int frameWidth = FramePixelWidth;
        int frameHeight = FramePixelHeight;

        // The whole-number scale gameplay renders at. The letterboxed presentation gets it from the
        // Pixel Perfect Camera for free, but the filled presentation below is derived from it - and it
        // is the number the UI follows.
        int gameplayScale = GameplayPixelScale;

        // Two presentations, and both of them keep art pixels a whole number of screen pixels wide:
        //  - FixedFrame renders the frame 1:1 into a reference of its own and lets the Pixel Perfect
        //    Camera blit it up by an integer zoom, with bars filling the rest of the screen.
        //  - FillScreen makes the window itself the reference and does the zoom in the camera's ortho
        //    size instead, so the whole screen is world at exactly that integer scale. Reference and
        //    window being equal keeps the Pixel Perfect Camera's pixel ratio at 1, so it never crops
        //    and never corrects the lens behind us.
        bool fillScreen = sceneFillScreen || fit == ViewFit.FillScreen;
        bool frameFits = Screen.width >= frameWidth && Screen.height >= frameHeight;

        // Below the frame's own size, a fixed frame falls back to showing the window 1:1 - crisp, a
        // little less world, never a fractional rescale of the art.
        bool windowAtOneToOne = !fillScreen && !frameFits;
        bool letterbox = !fillScreen && frameFits;

        int artScale = 1;
        int referenceWidth;
        int referenceHeight;

        if (fillScreen)
        {
            // The world map keeps a whole-number art scale, but only an even one: it is authored at
            // twice the gameplay frame, so the even half of gameplay's zoom is the largest scale at
            // which its tiles still land on whole art pixels while it shows twice as much world.
            // Gameplay at 3x therefore renders the map at 1x, and gameplay at 4x or 5x at 2x. Deriving
            // it from gameplay rather than from the screen is what ties the two together: the map can
            // never land on a scale gameplay could not reach.
            int evenGameplayScale = gameplayScale - (gameplayScale % 2);
            artScale = Mathf.Max(1, evenGameplayScale / 2);
            referenceWidth = Mathf.Max(2, Screen.width);
            referenceHeight = Mathf.Max(2, Screen.height);
        }
        else if (windowAtOneToOne)
        {
            referenceWidth = Mathf.Max(2, Screen.width);
            referenceHeight = Mathf.Max(2, Screen.height);
        }
        else
        {
            referenceWidth = frameWidth;
            referenceHeight = frameHeight;
        }

        // Half the reference height in world units, divided by the integer art scale so a filled screen
        // shows exactly `artScale` screen pixels per art pixel. Because the reference is a whole number
        // of art pixels and the scale is whole, the Pixel Perfect Camera's own zoom correction stays at
        // 1x and never divides the value we ask for.
        float targetOrthoSize = referenceHeight * 0.5f / (ppu * artScale);

        LensSettings lens = cinemachineCamera.Lens;
        if (lens.ModeOverride != LensSettings.OverrideModes.Orthographic ||
            !Mathf.Approximately(lens.OrthographicSize, targetOrthoSize))
        {
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = targetOrthoSize;
            cinemachineCamera.Lens = lens;
        }

        // Windowbox + upscale only when the frame is actually letterboxed; a filled screen and the
        // 1:1 fallback both render natively. `letterbox` is computed above, where the reference is
        // chosen, because the camera-rect normalisation below needs it too.
        PixelPerfectCamera pixelPerfect = ResolvePixelPerfectCamera();
        if (pixelPerfect != null)
        {
            if (pixelPerfect.assetsPPU != ppu)
                pixelPerfect.assetsPPU = ppu;
            if (pixelPerfect.refResolutionX != referenceWidth)
                pixelPerfect.refResolutionX = referenceWidth;
            if (pixelPerfect.refResolutionY != referenceHeight)
                pixelPerfect.refResolutionY = referenceHeight;

            PixelPerfectCamera.CropFrame cropFrame = letterbox
                ? PixelPerfectCamera.CropFrame.Windowbox
                : PixelPerfectCamera.CropFrame.None;
            if (pixelPerfect.cropFrame != cropFrame)
                pixelPerfect.cropFrame = cropFrame;

            PixelPerfectCamera.GridSnapping gridSnapping = letterbox
                ? PixelPerfectCamera.GridSnapping.UpscaleRenderTexture
                : PixelPerfectCamera.GridSnapping.PixelSnapping;
            if (pixelPerfect.gridSnapping != gridSnapping)
                pixelPerfect.gridSnapping = gridSnapping;
        }

        // A Pixel Perfect Camera writes Camera.rect while it renders (that is how it windowboxes),
        // and in edit mode that write lands in the scene file as a stray cropped viewport. It is
        // invisible while the Pixel Perfect Camera keeps rewriting it, and then bites the moment the
        // camera renders without one - which is exactly the planned smooth UI camera. Whenever this
        // rig is not the thing letterboxing, the camera owns the whole window.
        Camera camera = OutputCamera;
        Rect fullViewport = new Rect(0f, 0f, 1f, 1f);
        if (camera != null && !letterbox && camera.rect != fullViewport)
            camera.rect = fullViewport;

        // The confiner caches its solution per lens size, so a view change invalidates it.
        if (confiner != null && confiner.BoundingShape2D != null)
            confiner.InvalidateBoundingShapeCache();

        // A frame that does not fit the window is the usual reason pixel art suddenly looks broken,
        // and it is invisible from inside the game, so say it once per window size.
        string screenSize = Screen.width + "x" + Screen.height;
        if (windowAtOneToOne && lastFallbackLog != screenSize)
        {
            lastFallbackLog = screenSize;
            Debug.LogWarning($"[PersistentCameraController] The {frameWidth}x{frameHeight} px frame does not fit the " +
                             $"{screenSize} px window, so the view is showing the window at 1:1 (slightly less world, " +
                             $"pixels intact). Enlarge the window/Game view to at least {frameWidth}x{frameHeight} " +
                             $"for the full frame.", this);
        }
        else if (!windowAtOneToOne)
        {
            lastFallbackLog = null;
        }
    }

    // Applies a scene-declared frame without waiting for a scene load. BindToScene calls this on
    // every load, and CameraFrameOverride calls it while its scene is open in the editor so the
    // preview matches what the game will show. Passing null reverts to the serialized gameplay frame.
    public void ApplySceneFrame(CameraFrameOverride frameOverride)
    {
        hasSceneFrame = frameOverride != null;
        sceneFillScreen = frameOverride != null && frameOverride.FillScreen;
        if (hasSceneFrame)
        {
            sceneTilesHorizontal = frameOverride.VisibleTilesHorizontal;
            sceneTilesVertical = frameOverride.VisibleTilesVertical;
        }

        ApplyViewSettings();
    }

    // A scene can ask for a camera that snaps to its target instead of easing after it: the world map
    // moves the player in deliberate hops, and a damped camera would still be catching up when the
    // next hop starts. Only applied while playing, so the authored damping is what stays in the scene.
    private void ApplyFollowDamping(CameraFrameOverride frameOverride)
    {
        if (positionComposer == null || !Application.isPlaying)
            return;

        float damping = frameOverride != null ? frameOverride.FollowDamping : -1f;
        Vector3 target = damping >= 0f
            ? new Vector3(damping, damping, authoredDamping.z)
            : authoredDamping;

        if (positionComposer.Damping != target)
            positionComposer.Damping = target;
    }

    // --- Follow override -------------------------------------------------------------------
    // The main menu tours the camera around the world map while its menu is up. Rather than
    // moving the camera behind Cinemachine's back, the rig follows a temporary target for the
    // duration of the tour and then hands the framing back to the scene's own target (the
    // player, or a CameraFrameOverride.Focus). The frame override, lens and confiner are left
    // exactly as the scene set them, so returning does not change the zoom or the bounds.
    private bool hasFollowOverride;
    private Transform followBeforeOverride;
    private Vector3 dampingBeforeOverride;

    // What the rig is looking at right now (the roam target while a menu tour is running).
    public Transform FollowTarget => cinemachineCamera != null ? cinemachineCamera.Follow : null;

    // The scene's own target, ignoring an active override. The menu recenter asks for this.
    public Transform DefaultFollow => hasFollowOverride ? followBeforeOverride : FollowTarget;

    public void SetFollowOverride(Transform target)
    {
        if (cinemachineCamera == null || target == null)
            return;

        if (!hasFollowOverride)
        {
            followBeforeOverride = cinemachineCamera.Follow;
            dampingBeforeOverride = positionComposer != null ? positionComposer.Damping : Vector3.zero;
            hasFollowOverride = true;
        }

        cinemachineCamera.Follow = target;
        cinemachineCamera.LookAt = target;

        // The roam path is already smooth; damping on top of it would drag the camera behind
        // the tour and make the framing lurch at every waypoint.
        if (positionComposer != null)
            positionComposer.Damping = Vector3.zero;
    }

    // Hands the camera back to the scene's own target. snap teleports the framing instead of
    // letting the lens ease across the map; callers usually have their own recenter tween.
    public void ClearFollowOverride(bool snap = true)
    {
        if (cinemachineCamera == null)
            return;

        if (hasFollowOverride)
        {
            cinemachineCamera.Follow = followBeforeOverride;
            cinemachineCamera.LookAt = followBeforeOverride;
            if (positionComposer != null)
                positionComposer.Damping = dampingBeforeOverride;
        }

        hasFollowOverride = false;
        followBeforeOverride = null;

        if (snap)
            cinemachineCamera.CancelDamping(true);
    }


    private static CameraFrameOverride FindLoadedSceneFrameOverride()
    {
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded || scene.name == PersistentSceneName)
                continue;

            CameraFrameOverride frameOverride = FindComponentInScene<CameraFrameOverride>(scene);
            if (frameOverride != null)
                return frameOverride;
        }

        return null;
    }

    private PixelPerfectCamera ResolvePixelPerfectCamera()
    {
        if (pixelPerfectCamera != null)
            return pixelPerfectCamera;

        Camera camera = OutputCamera;
        if (camera != null)
            pixelPerfectCamera = camera.GetComponent<PixelPerfectCamera>();

        // Before the brain has resolved its output camera (fresh load, or edit mode).
        if (pixelPerfectCamera == null)
            pixelPerfectCamera = FindAnyObjectByType<PixelPerfectCamera>();

        return pixelPerfectCamera;
    }

    private Camera ResolveOutputCamera()
    {
        CinemachineBrain brain = cinemachineCamera != null
            ? CinemachineCore.FindPotentialTargetBrain(cinemachineCamera)
            : null;

        Camera camera = brain != null ? brain.OutputCamera : null;
        return camera != null ? camera : Camera.main;
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
        // A scene may ask for a different frame (the world map is authored at twice the gameplay
        // scale) and may also say what the camera should look at instead of the player.
        CameraFrameOverride frameOverride = FindComponentInScene<CameraFrameOverride>(scene);

        // Re-resolve the rendering camera: the incoming scene may have changed what Camera.main is.
        outputCamera = null;
        pixelPerfectCamera = null;

        // Apply before binding: the confiner solves its caches against the current lens size.
        ApplySceneFrame(frameOverride);

        Transform follow = frameOverride != null ? frameOverride.Focus : null;
        if (follow == null)
        {
            PlayerController player = FindComponentInScene<PlayerController>(scene);
            if (player == null)
            {
                Debug.LogWarning($"Persistent camera could not find a {nameof(PlayerController)} in scene '{scene.name}'.", this);
                return;
            }

            follow = player.transform;
        }

        // A scene load owns the follow target again: a menu tour must not survive into the next
        // scene, and its remembered player belongs to the scene being unloaded.
        hasFollowOverride = false;
        followBeforeOverride = null;

        cinemachineCamera.Follow = follow;
        cinemachineCamera.LookAt = follow;
        ApplyFollowDamping(frameOverride);

        // A missing confiner means "unconfined", not "keep the last level's bounds": the world map
        // has no CameraConfiner, and a stale shape would clamp the camera to geometry it has not got.
        Collider2D boundingShape = FindConfinerInScene(scene);
        confiner.BoundingShape2D = boundingShape;
        confiner.InvalidateBoundingShapeCache();
        cinemachineCamera.CancelDamping(true);

        WarnAboutExtraRigs(scene);

        // The presentation that is actually applied, not the serialized default: the world map asks for
        // FillScreen through its override, and a frame too big for the window falls back to 1:1.
        bool fillScreen = sceneFillScreen || fit == ViewFit.FillScreen;
        bool frameFits = Screen.width >= FramePixelWidth && Screen.height >= FramePixelHeight;
        string presentation = fillScreen ? "FillScreen" : frameFits ? "FixedFrame" : "window at 1:1";

        Debug.Log($"[PersistentCameraController] {scene.name}: {FramePixelWidth}x{FramePixelHeight} px " +
                  $"({ActiveTilesHorizontal}x{ActiveTilesVertical} tiles, {presentation}, gameplay zoom {GameplayPixelScale}x)", this);
    }

    // Two cameras at the same depth with a brain each render over one another and the winner depends
    // on draw order - which is exactly how the menus ended up at a different size from the game. The
    // rig lives in Persistent; a scene that ships its own is a bug, so name it loudly.
    private void WarnAboutExtraRigs(Scene scene)
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (cameras.Length <= 1)
            return;

        System.Text.StringBuilder offenders = new System.Text.StringBuilder();
        foreach (Camera camera in cameras)
        {
            if (offenders.Length > 0)
                offenders.Append(", ");

            offenders.Append(camera.name).Append(' ').Append('(').Append(camera.gameObject.scene.name).Append(')');
        }

        Debug.LogWarning($"[PersistentCameraController] {cameras.Length} active cameras after loading " +
                         $"'{scene.name}': {offenders}. Only the Persistent rig should have a camera.", this);
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
