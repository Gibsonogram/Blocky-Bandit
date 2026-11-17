using UnityEngine;

// A scene-level override for the persistent camera rig. The rig lives in the Persistent scene and
// shows the gameplay frame (12 x 8 tiles at 32 PPU = 384x256 art pixels) unless the loaded scene
// declares otherwise. The world map uses it to ask for 24 x 16 tiles (768x512 art pixels): the tiles
// are still 32x32 texels drawn 1:1 into the art-pixel buffer, and the wider frame just halves the
// Pixel Perfect Camera's upscale, which is what makes the map read as twice as much world at half the
// on-screen tile size. Never widen the frame *and* stretch the tiles - the override replaces the
// frame; it does not rescale the world.
//
// PersistentCameraController discovers it the same way it discovers "CameraConfiner": by looking
// through the loaded scene, so nothing has to be wired up. Leaving it out means "use the gameplay
// frame". Because the override is only remembered until the next scene binds, the serialized values
// on the rig stay the default for every other scene.
//
// It also applies itself while not playing, so opening this scene alongside Persistent in the editor
// shows the same camera setup the game will, instead of the gameplay one.
//
// Everything here is optional:
//   - the tile counts, for a scene that shows more or less world than gameplay
//   - a focus transform, to stop following the player and park the camera somewhere else
//   - follow damping, for a screen that reads better with a camera that snaps between hops
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class CameraFrameOverride : MonoBehaviour
{
    [Header("Frame")]
    [Tooltip("Tiles visible horizontally. The gameplay frame is 12; the world map asks for 24, which " +
             "shows twice as much world at half the on-screen tile size (the tiles themselves stay " +
             "1:1 with art pixels).")]
    [SerializeField, Min(0.05f)] private float visibleTilesHorizontal = 12f;

    [Tooltip("Tiles visible vertically. 8 matches the 12 above for the gameplay frame; the world map " +
             "asks for 16.")]
    [SerializeField, Min(0.05f)] private float visibleTilesVertical = 8f;

    [Header("Presentation")]
    [Tooltip("Fill the window with world instead of letterboxing the frame. The window becomes the " +
             "reference resolution and the camera sets the whole-number art scale, so a wider screen " +
             "shows more world rather than bars, and the tile counts above act as a minimum. The " +
             "world map uses this. Leave off for the barred, exact-frame presentation gameplay uses.")]
    [SerializeField] private bool fillScreen;

    [Header("Focus")]
    [Tooltip("Transform the camera follows instead of the scene's PlayerController. Leave empty to " +
             "keep following the player.")]
    [SerializeField] private Transform focus;

    [Tooltip("Seconds of camera lag while following. 0 snaps the camera to its target, which is how " +
             "a screen of deliberate hops reads: the camera should arrive with the player. Negative " +
             "keeps the rig's authored gameplay damping.")]
    [SerializeField] private float followDamping = -1f;

    public float VisibleTilesHorizontal => Mathf.Max(0.05f, visibleTilesHorizontal);
    public float VisibleTilesVertical => Mathf.Max(0.05f, visibleTilesVertical);
    public Transform Focus => focus;
    public float FollowDamping => followDamping;
    public bool FillScreen => fillScreen;

    // Play mode is driven by PersistentCameraController.BindToScene, which owns scene loads; these
    // are only for the editor, where no scene-load event happens.
    private void OnEnable() => ApplyInEditor(this);

    private void OnDisable() => ApplyInEditor(null);

    private void OnValidate() => ApplyInEditor(this);

    private void ApplyInEditor(CameraFrameOverride frameOverride)
    {
        if (Application.isPlaying)
            return;

        PersistentCameraController rig = PersistentCameraController.Instance;
        if (rig != null)
            rig.ApplySceneFrame(frameOverride);
    }
}

