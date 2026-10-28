using System.Collections.Generic;
using UnityEngine;

// Draws the MagicMine's cross-shaped detonation across every tile its beam reached.
//
// The beam length is decided at detonation time (each arm stops at the first collision
// tile), so the pattern cannot be baked into a fixed sprite: one child SpriteRenderer is
// stamped per tile instead, all cloned from a template renderer so sorting, material and
// colour stay editable in the Inspector.
//
// Frame order is small -> large -> small. The centre tile uses the burst pose; every
// other tile uses the band pose, rotated 90 degrees on the vertical arms.
//
// The whole effect runs for 'duration' and then destroys its own GameObject, so the mine
// that spawned it does not need to track it.
public class MagicMineBoomFX : MonoBehaviour
{
    // Frame count is fixed at three; the arrays are indexed by frame.
    private const int FrameCount = 3;

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
    // Kept inactive on the prefab; cloned once per beam tile. Supplies sorting layer,
    // order, material and colour so the effect stays configurable without touching code.
    [SerializeField] private SpriteRenderer tileTemplate;

    private readonly List<SpriteRenderer> centerRenderers = new();
    private readonly List<SpriteRenderer> armRenderers = new();
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

        // The mine's own tile is excluded from the beam, so it is stamped separately.
        centerRenderers.Add(Stamp(GridUtils.GridToWorld(origin), Quaternion.identity));

        foreach (Vector2Int tile in beamTiles)
        {
            // Same row as the mine means the horizontal arm; anything else is vertical and
            // reuses the same band sprite turned on its side.
            bool isHorizontal = tile.y == origin.y;
            Quaternion rotation = isHorizontal
                ? Quaternion.identity
                : Quaternion.Euler(0f, 0f, 90f);

            armRenderers.Add(Stamp(GridUtils.GridToWorld(tile), rotation));
        }

        ApplyFrame(0);
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
    // three sprite assignments rather than one per tile per frame.
    private void ApplyFrame(int frame)
    {
        if (frame == currentFrame) return;
        currentFrame = frame;

        Sprite centerSprite = FrameSprite(centerFrames, frame);
        foreach (SpriteRenderer renderer in centerRenderers)
            renderer.sprite = centerSprite;

        Sprite armSprite = FrameSprite(armFrames, frame);
        foreach (SpriteRenderer renderer in armRenderers)
            renderer.sprite = armSprite;
    }

    // Tolerates a short or partly-empty array: the frame simply shows nothing rather than
    // throwing mid-explosion.
    private static Sprite FrameSprite(Sprite[] frames, int frame)
    {
        if (frames == null || frames.Length == 0) return null;
        return frames[Mathf.Min(frame, frames.Length - 1)];
    }
}