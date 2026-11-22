using UnityEngine;

// A second copy of the player sprite, tinted dark, that shows up only on the pixels the
// walk-behind tilemap actually paints.
//
// The walk-behind tilemap renders with Custom/WalkBehindStencil, which marks stencil bit 128
// under its opaque pixels, and this renderer uses Custom/PlayerShadow, which only draws where
// that mark exists. Every other pixel keeps the normal sprite. The mask setup and the frame
// mirroring live in WalkBehindShadow so the collectables can reuse the exact same system.
//
// The shaders come from Resources so the levels keep their authored materials and no scene
// or prefab needs to change.
public class PlayerShadow : MonoBehaviour
{
    [SerializeField] private SpriteRenderer playerSpriteRenderer;

    private SpriteRenderer shadowRenderer;
    private bool hasMask;

    private void Awake()
    {
        shadowRenderer = GetComponent<SpriteRenderer>();
        InstallMask();
    }

    // Late update because we want Update to run Animator on the player sprite first.
    private void LateUpdate()
    {
        // Without the mask the silhouette would cover the whole sprite, so stay hidden.
        if (!hasMask)
            return;

        WalkBehindShadow.Mirror(playerSpriteRenderer, shadowRenderer);
    }

    private void InstallMask()
    {
        hasMask = WalkBehindShadow.TryInstall(shadowRenderer);
        if (hasMask)
            return;

        // Without the stencil the silhouette would cover the whole player, which is the look
        // this component exists to avoid. Stay hidden rather than draw that.
        if (shadowRenderer != null)
            shadowRenderer.enabled = false;
        Debug.LogWarning(
            $"{name}: player shadow disabled, missing SpriteRenderer, {WalkBehindShadow.MaskMaterialResource}/{WalkBehindShadow.ShadowMaterialResource} material or the walk-behind tilemap.",
            this);
    }
}

