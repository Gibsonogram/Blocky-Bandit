// The player-facing danger-overlay defaults. The Visual settings screen seeds its controls from
// these, its Reset button restores them, and SaveManager falls back to them for a player who has
// never touched the screen. The values themselves live on the VisionOverlayStyle asset
// (Player defaults), so tuning them in the Inspector is enough - nothing here copies a number.
public static class VisionOverlayDefaults
{
    // Overall opacity of the overlay; whole-percent granularity, matching the slider.
    public static float Opacity => VisionOverlayRenderer.Style.defaultOpacity;

    // Indicator radius in world units (one tile is 1 unit). Masks are rasterized at whole art
    // pixels, so values a fraction of a pixel apart can produce the same size.
    public static float Radius => VisionOverlayRenderer.Style.defaultRadius;

    // The range the size sliders offer. Fixed and shared: the Visual settings slider, the
    // overlay's custom Inspector and SaveManager's clamp all read these, so a change here moves
    // every control at once. Anything a player saved outside the range is clamped on read.
    public const float MinRadius = 0.1f;
    public const float MaxRadius = 0.25f;
}
