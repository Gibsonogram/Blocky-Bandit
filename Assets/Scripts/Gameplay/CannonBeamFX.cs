using System.Collections.Generic;
using UnityEngine;

// Draws a cannon's firing lane in two parts, matching the direction-specific art:
//   - the muzzle pose on the tile the cannon's art occupies (_0 on the first small beat, _1
//     on the large beat, then _0 again), and
//   - the shared MagicMine band tiled down every other lane tile, horizontal for a sideways
//     shot and rotated vertical for an up/down shot.
//
// Like MagicMineBoomFX the cost is independent of lane length: one renderer for the muzzle and
// one tiled strip for the band, however long the lane is. The effect owns its own lifetime and
// destroys its own GameObject, so the cannon that spawned it does not need to track it.
public class CannonBeamFX : MonoBehaviour
{
    [Header("Band Frames (small, large, small)")]
    [Tooltip("Shared lane band, tiled once per tile. These are the poses MagicMineBoomFX uses " +
             "for its arms, so a cannon shot and a mine blast read as the same event.")]
    [SerializeField] private Sprite[] bandFrames = new Sprite[3];

    // Relative time per frame: small, large, small. Matches MagicMineBoomFX. The muzzle
    // follows these same beats, so this is the single timeline for the whole effect.
    [Tooltip("Relative hold time per band frame. Normalised, so only the ratios matter.")]
    [SerializeField] private float[] bandFrameWeights = { 0.3f, 0.4f, 0.3f };

    [Header("Timing")]
    // Matches MagicMineBoomFX and ExplodeClip, so every blast reads as one event.
    [Tooltip("Total lifetime in seconds.")]
    [SerializeField] private float duration = 0.21666667f;

    [Header("Rendering")]
    // Kept inactive on the prefab; cloned once for the muzzle and once for the band. Supplies
    // sorting layer, order, material and colour so the effect stays configurable in the
    // Inspector.
    [SerializeField] private SpriteRenderer tileTemplate;

    private SpriteRenderer muzzleRenderer;
    private SpriteRenderer bandRenderer;
    private Sprite[] muzzleFrames;
    private int bandLength;
    private bool bandVertical;
    private float elapsed;
    private int bandFrame = -1;
    private int muzzleFrame = -1;
    private bool built;

    // origin is the tile the cannon's visual occupies; direction is the beam's travel
    // direction and laneTiles is the set the danger overlay paints (so the band ends exactly
    // where the lane stops). The muzzle covers origin itself; the band starts on the next tile.
    public void Show(Vector2Int origin, Vector2Int direction, IEnumerable<Vector2Int> laneTiles,
                     Sprite[] muzzle)
    {
        // Guard against a second call building a duplicate beam on top of the first.
        if (built) return;

        if (tileTemplate == null)
        {
            Debug.LogWarning("CannonBeamFX: no tileTemplate assigned; nothing to draw.", this);
            return;
        }
        built = true;
        muzzleFrames = muzzle;

        // The lane is one straight run, so the band only needs its length: everything past the
        // muzzle tile is covered by a single tiled strip.
        int length = 0;
        foreach (Vector2Int tile in laneTiles)
        {
            int along = (tile.x - origin.x) * direction.x + (tile.y - origin.y) * direction.y;
            if (along > 0) length++;
        }
        bandLength = length;
        bandVertical = direction.x == 0;

        muzzleRenderer = Stamp(GridUtils.GridToWorld(origin), Quaternion.identity);

        if (bandLength > 0)
        {
            // Centred on the strip's midpoint, so the first repeat lands in the first band tile
            // and the last in the far end of the lane.
            Vector3 center = GridUtils.GridToWorld(origin)
                             + new Vector3(direction.x, direction.y, 0f) * (0.5f + bandLength * 0.5f);
            Quaternion rotation = bandVertical ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;

            bandRenderer = Stamp(center, rotation);
            Sprite band = FrameSprite(bandFrames, 0);
            // A sprite must be present before a draw mode can be chosen, and the size only
            // applies once the renderer is tiled; order matters here.
            bandRenderer.sprite = band;
            bandRenderer.drawMode = SpriteDrawMode.Tiled;
            bandRenderer.tileMode = SpriteTileMode.Continuous;
            ApplyBandSize(band);
        }

        ApplyFrames(0f);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        float progress = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
        ApplyFrames(progress);

        if (elapsed >= duration)
            Destroy(gameObject);
    }

    // Only touches a renderer when its frame actually changes, so a long lane costs two sprite
    // assignments per frame rather than one per tile.
    //
    // Both pieces follow the band's weighted beats (small -> large -> small). The muzzle has
    // fewer poses than there are beats, so its frame index wraps: _0 on the first small beat,
    // _1 on the large beat, then _0 again as the beam collapses. Driving it from the same
    // timeline - instead of its own even split - is what stops the muzzle still holding the
    // large pose after every other tile has shrunk back.
    private void ApplyFrames(float progress)
    {
        int phase = PhaseIndex(progress, bandFrameWeights);

        int bandCount = bandFrames != null ? bandFrames.Length : 0;
        if (bandRenderer != null && bandCount > 0 && phase >= 0)
        {
            int frame = phase % bandCount;
            if (frame != bandFrame)
            {
                bandFrame = frame;
                Sprite sprite = FrameSprite(bandFrames, frame);
                bandRenderer.sprite = sprite;
                ApplyBandSize(sprite);
            }
        }

        int muzzleCount = muzzleFrames != null ? muzzleFrames.Length : 0;
        if (muzzleRenderer != null && muzzleCount > 0 && phase >= 0)
        {
            int frame = phase % muzzleCount;
            if (frame != muzzleFrame)
            {
                muzzleFrame = frame;
                muzzleRenderer.sprite = FrameSprite(muzzleFrames, frame);
            }
        }
    }

    // Tiled strips are sized in world units, so one repeat of a one-tile sprite per tile keeps
    // the band aligned to the grid even when a pose has a different size. Derived from the
    // sprite bounds rather than assuming one unit.
    private void ApplyBandSize(Sprite sprite)
    {
        if (bandRenderer == null) return;

        float width = sprite != null ? Mathf.Max(sprite.bounds.size.x, 0.0001f) : 1f;
        float height = sprite != null ? Mathf.Max(sprite.bounds.size.y, 0.0001f) : 1f;
        bandRenderer.size = new Vector2(width * bandLength, height);
    }

    private SpriteRenderer Stamp(Vector3 worldPosition, Quaternion rotation)
    {
        SpriteRenderer renderer = Instantiate(tileTemplate, worldPosition, rotation, transform);
        renderer.gameObject.SetActive(true);
        renderer.sprite = null;
        return renderer;
    }

    // Maps normalised progress onto the weighted beat list. Allocation-free: called every
    // frame. Degenerate (all-zero) weights fall back to an even split rather than showing
    // nothing. Returns -1 when there are no beats at all.
    private static int PhaseIndex(float progress, float[] weights)
    {
        if (weights == null || weights.Length == 0) return -1;

        float total = 0f;
        for (int i = 0; i < weights.Length; i++)
            total += FrameWeight(weights, i);

        if (total <= 0f)
            return Mathf.Min((int)(progress * weights.Length), weights.Length - 1);

        float accumulated = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            accumulated += FrameWeight(weights, i) / total;
            if (progress < accumulated) return i;
        }

        return weights.Length - 1;
    }

    private static float FrameWeight(float[] weights, int frame)
    {
        if (weights == null || frame >= weights.Length) return 1f;
        return Mathf.Max(0f, weights[frame]);
    }

    // Tolerates a short or partly-empty array: the frame simply shows nothing rather than
    // throwing mid-explosion.
    private static Sprite FrameSprite(Sprite[] frames, int frame)
    {
        if (frames == null || frames.Length == 0 || frame < 0) return null;
        return frames[Mathf.Min(frame, frames.Length - 1)];
    }
}
