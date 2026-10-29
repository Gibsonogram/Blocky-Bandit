using System.Collections.Generic;
using UnityEngine;

// Draws the MagicMine's cross-shaped detonation across every tile its beam reached.
//
// The beam length is decided at detonation time (each arm stops at the first collision
// tile), so the pattern cannot be baked into a fixed sprite. It is drawn as five pieces:
// one burst on the mine's own tile plus one strip per arm, each strip a single tiled
// SpriteRenderer that repeats the band pose once per tile - the same pixels the old
// one-renderer-per-tile stamping drew. Everything is cloned from a template renderer so
// sorting, material and colour stay editable in the Inspector.
//
// The cost is deliberately independent of the beam length. Stamping a renderer per tile
// meant a full-length beam instantiated 240 GameObjects and 240 SpriteRenderers inside a
// single detonation frame, then rebuilt 240 sprite meshes on every animation frame; on a
// chained blast that hitch is repeated per mine. Five renderers draw the same cross.
//
// Frame order is small -> large -> small. The centre tile uses the burst pose; the arms
// use the band pose, rotated 90 degrees on the vertical arms.
//
// The whole effect runs for 'duration' and then destroys its own GameObject, so the mine
// that spawned it does not need to track it.
public class MagicMineBoomFX : MonoBehaviour
{
    // Frame count is fixed at three; the arrays are indexed by frame.
    private const int FrameCount = 3;

    // Centre plus the four arms: the most pieces a cross can ever need.
    private const int MaxPieces = 1 + 4;

    [Header("Frames (small, large, small)")]
    [SerializeField] private Sprite[] centerFrames = new Sprite[FrameCount];
    [SerializeField] private Sprite[] armFrames = new Sprite[FrameCount];

    [Header("Timing")]
    // Matches ExplodeClip, the basic mine's explosion, so both blasts read as one event.
    [Tooltip("Total lifetime in seconds.")]
    [SerializeField] private float duration = 0.21666667f;

    // Relative time per frame: small, large, small. The middle (large) frame is given a
    // little more time so the widest pose registers before it snaps back. Weights are
    // normalised, so they can be any positive numbers and the total stays 'duration'.
    [Tooltip("Relative hold time per frame. Normalised, so only the ratios matter.")]
    [SerializeField] private float[] frameWeights = { 0.3f, 0.4f, 0.3f };

    [Header("Rendering")]
    // Kept inactive on the prefab; cloned once per cross piece (centre + each arm). Supplies
    // sorting layer, order, material and colour so the effect stays configurable without
    // touching code.
    [SerializeField] private SpriteRenderer tileTemplate;

    private readonly List<SpriteRenderer> centerRenderers = new();
    private readonly List<SpriteRenderer> armRenderers = new();

    // Arm strips are re-sized on every frame change (the poses can be different sizes), so
    // each strip remembers the reach it was built for. A strip's length is its tile count.
    private readonly List<int> armLengths = new();
    private float elapsed;
    private int currentFrame = -1;
    private bool built;

    // Builds the cross at the given tiles. Called by MagicMine before it destroys itself,
    // so this object must already be parented at the mine's position in world space.
    public void Show(Vector2Int origin, IEnumerable<Vector2Int> beamTiles)
    {
        // Guard against a second call building a duplicate cross on top of the first.
        if (built) return;

        if (tileTemplate == null)
        {
            Debug.LogWarning("MagicMineBoomFX: no tileTemplate assigned; nothing to draw.", this);
            return;
        }
        built = true;

        // Reduce the tile set to how far each arm reaches: the beam is a cross through the
        // mine, so every tile lies on exactly one arm and that arm's far end is all the
        // geometry needs. This keeps a full-length blast (240 tiles) to five renderers.
        int right = 0, left = 0, up = 0, down = 0;
        foreach (Vector2Int tile in beamTiles)
        {
            int dx = tile.x - origin.x;
            int dy = tile.y - origin.y;
            if (dy == 0 && dx != 0)
            {
                if (dx > right) right = dx;
                if (-dx > left) left = -dx;
            }
            else if (dx == 0 && dy != 0)
            {
                if (dy > up) up = dy;
                if (-dy > down) down = -dy;
            }
        }

        // The mine's own tile is excluded from the beam, so it is stamped separately.
        centerRenderers.Add(Stamp(GridUtils.GridToWorld(origin), Quaternion.identity));

        AddArm(origin, right, Vector2Int.right);
        AddArm(origin, left, Vector2Int.left);
        AddArm(origin, up, Vector2Int.up);
        AddArm(origin, down, Vector2Int.down);

        ApplyFrame(0);
    }

    // One strip per arm. The strip is centred on the arm's midpoint and sized to the arm's
    // length, so a tiled renderer repeats the band pose exactly once per tile - including
    // the half-tile offset that puts the first repeat in the first tile. Vertical arms use
    // the same sprite turned on its side; Tiled draw mode makes the strip's local X the
    // repeat axis, so the rotation is all that changes.
    private void AddArm(Vector2Int origin, int length, Vector2Int direction)
    {
        if (length <= 0 || armRenderers.Count >= MaxPieces - 1) return;

        Vector3 center = GridUtils.GridToWorld(origin)
                         + new Vector3(direction.x, direction.y, 0f) * (0.5f + length * 0.5f);
        Quaternion rotation = direction.x != 0
            ? Quaternion.identity
            : Quaternion.Euler(0f, 0f, 90f);

        SpriteRenderer renderer = Stamp(center, rotation);
        // A sprite must be present before a draw mode can be chosen, and the size only
        // applies once the renderer is tiled; order matters here.
        Sprite sprite = FrameSprite(armFrames, 0);
        renderer.sprite = sprite;
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.tileMode = SpriteTileMode.Continuous;
        ApplyArmSize(renderer, length, sprite);

        armRenderers.Add(renderer);
        armLengths.Add(length);
    }

    // Tiled strips are sized in world units, so 'length' repeats of a one-tile sprite is a
    // strip exactly 'length' tiles long. Derived from the sprite bounds rather than assuming
    // one unit, so re-imported art at a different scale still lines up with the grid.
    private static void ApplyArmSize(SpriteRenderer renderer, int length, Sprite sprite)
    {
        float width = sprite != null ? Mathf.Max(sprite.bounds.size.x, 0.0001f) : 1f;
        float height = sprite != null ? Mathf.Max(sprite.bounds.size.y, 0.0001f) : 1f;
        renderer.size = new Vector2(width * length, height);
    }

    private SpriteRenderer Stamp(Vector3 worldPosition, Quaternion rotation)
    {
        SpriteRenderer renderer = Instantiate(tileTemplate, worldPosition, rotation, transform);
        renderer.gameObject.SetActive(true);
        renderer.sprite = null;
        return renderer;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        float progress = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
        ApplyFrame(FrameForProgress(progress));

        if (elapsed >= duration)
            Destroy(gameObject);
    }

    // Maps normalised progress to a frame index using the per-frame weights, so the large
    // pose can be held longer than the two small ones. Allocation-free: called per frame.
    private int FrameForProgress(float progress)
    {
        float total = 0f;
        for (int i = 0; i < FrameCount; i++)
            total += FrameWeight(i);

        // Degenerate weights fall back to an even split rather than showing nothing.
        if (total <= 0f)
            return Mathf.Min((int)(progress * FrameCount), FrameCount - 1);

        float accumulated = 0f;
        for (int i = 0; i < FrameCount; i++)
        {
            accumulated += FrameWeight(i) / total;
            if (progress < accumulated) return i;
        }

        return FrameCount - 1;
    }

    private float FrameWeight(int frame)
    {
        if (frameWeights == null || frame >= frameWeights.Length) return 1f;
        return Mathf.Max(0f, frameWeights[frame]);
    }

    // Only touches the renderers when the frame actually changes, so a long beam costs
    // five sprite assignments rather than one per tile per frame.
    private void ApplyFrame(int frame)
    {
        if (frame == currentFrame) return;
        currentFrame = frame;

        Sprite centerSprite = FrameSprite(centerFrames, frame);
        foreach (SpriteRenderer renderer in centerRenderers)
            if (renderer != null) renderer.sprite = centerSprite;

        Sprite armSprite = FrameSprite(armFrames, frame);
        for (int i = 0; i < armRenderers.Count; i++)
        {
            SpriteRenderer renderer = armRenderers[i];
            if (renderer == null) continue;
            renderer.sprite = armSprite;

            // Poses can differ in size, and a tiled strip's length is in world units, so
            // re-size it whenever the sprite changes to keep one repeat per tile.
            if (i < armLengths.Count)
                ApplyArmSize(renderer, armLengths[i], armSprite);
        }
    }

    // Tolerates a short or partly-empty array: the frame simply shows nothing rather than
    // throwing mid-explosion.
    private static Sprite FrameSprite(Sprite[] frames, int frame)
    {
        if (frames == null || frames.Length == 0) return null;
        return frames[Mathf.Min(frame, frames.Length - 1)];
    }
}