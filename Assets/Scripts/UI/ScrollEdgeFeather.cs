using UnityEngine;
using UnityEngine.UI;

// Feathers the ends of the level list so rows dissolve into the panel instead of being cut off by a
// hard mask edge.
//
// RectMask2D already knows how to soften its own edge - CanvasRenderer.clippingSoftness fades every
// graphic the mask touches, images and TMP text alike, without a shader or an overlay. Its units are
// not documented though, so they were measured on this panel: the fade is linear and a softness of
// N covers about N / 2.7 canvas pixels, which is what FeatherDistance converts from. That makes the
// inspector number the distance the rows fade across, in the same canvas units the list is laid out
// in (row 150, viewport 520), so it scales with the UI rather than with the window.
//
// RectMask2D softens both sides of an axis equally, which is exactly what the list wants: the top
// and bottom edges feather by the same amount. The horizontal softness is pinned to 0 - the rows sit
// inside the parchment, so a sideways fade would only eat their edges.
[RequireComponent(typeof(RectMask2D))]
public class ScrollEdgeFeather : MonoBehaviour
{
    // Measured on the level list: 200 puts the fade's half-way point ~30 canvas pixels from the
    // edge, 600 ~110, 120 ~15 - i.e. the fade spans roughly softness / 2.7 canvas pixels.
    private const float SoftnessPerCanvasPixel = 2.7f;

    [Tooltip("Distance in canvas pixels over which rows fade out at the top and bottom of the list.")]
    [SerializeField] private float featherDistance = 48f;

    private RectMask2D mask;
    private int appliedSoftness = -1;

    private void Awake()
    {
        mask = GetComponent<RectMask2D>();
    }

    private void OnEnable()
    {
        Apply();
    }

    private void Update()
    {
        // The panel is only visible while the game runs, so this is where a value typed into the
        // inspector takes effect; the compare inside Apply keeps the cost to an int check with no
        // writes, and the component only ticks while the screen is open.
        Apply();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            Apply();
    }

    private void Apply()
    {
        if (mask == null)
            mask = GetComponent<RectMask2D>();
        if (mask == null)
            return;

        int softness = Mathf.RoundToInt(Mathf.Max(0f, featherDistance) * SoftnessPerCanvasPixel);
        if (softness == appliedSoftness)
            return;

        appliedSoftness = softness;
        mask.softness = new Vector2Int(0, softness);
    }
}
