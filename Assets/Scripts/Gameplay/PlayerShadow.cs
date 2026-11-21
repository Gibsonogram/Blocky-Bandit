using UnityEngine;
using UnityEngine.Tilemaps;

// A second copy of the player sprite, tinted dark, that shows up only on the pixels the
// walk-behind tilemap actually paints.
//
// The old version switched the whole silhouette on whenever the player stepped into a
// walk-behind cell, so the player turned black even where the art (tree canopies, arch tops)
// is transparent. Now the walk-behind tilemap renders with Custom/WalkBehindStencil, which
// marks stencil bit 128 under its opaque pixels, and this renderer uses Custom/PlayerShadow,
// which only draws where that mark exists. Every other pixel keeps the normal sprite.
//
// The shaders come from Resources so the levels keep their authored materials and no scene
// or prefab needs to change.
public class PlayerShadow : MonoBehaviour
{
    private const string MaskMaterialResource = "WalkBehindStencil";
    private const string ShadowMaterialResource = "PlayerShadow";

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
        MirrorPlayerSprite();
    }

    private void InstallMask()
    {
        Material maskMaterial = Resources.Load<Material>(MaskMaterialResource);
        Material shadowMaterial = Resources.Load<Material>(ShadowMaterialResource);
        Tilemap walkBehind = GridSettings.WalkBehindTilemap;
        TilemapRenderer tilemapRenderer = walkBehind != null
            ? walkBehind.GetComponent<TilemapRenderer>()
            : null;

        if (shadowRenderer == null || maskMaterial == null || shadowMaterial == null || tilemapRenderer == null)
        {
            // Without the stencil the silhouette would cover the whole player, which is the
            // look this component exists to avoid. Stay hidden rather than draw that.
            if (shadowRenderer != null)
                shadowRenderer.enabled = false;
            Debug.LogWarning(
                $"{name}: player shadow disabled, missing SpriteRenderer, {MaskMaterialResource}/{ShadowMaterialResource} material or the walk-behind tilemap.",
                this);
            return;
        }

        tilemapRenderer.sharedMaterial = maskMaterial;
        shadowRenderer.sharedMaterial = shadowMaterial;

        // Sit directly above the walk-behind art so the masked pixels land on top of it.
        shadowRenderer.sortingLayerID = tilemapRenderer.sortingLayerID;
        shadowRenderer.sortingOrder = tilemapRenderer.sortingOrder + 1;

        hasMask = true;
        shadowRenderer.enabled = true;
    }

    private void MirrorPlayerSprite()
    {
        if (shadowRenderer == null)
            return;

        // The player sprite is destroyed on defeat while this shadow's parent survives.
        if (playerSpriteRenderer == null)
        {
            SetShadowActive(false);
            return;
        }

        // Mirror nothing while the player's visual is hidden (defeat) either.
        bool playerVisible = playerSpriteRenderer.enabled && playerSpriteRenderer.gameObject.activeInHierarchy;
        SetShadowActive(hasMask && playerVisible);
        if (!playerVisible)
            return;

        // Copy only when the frame actually changed: re-assigning the same sprite every frame
        // dirties the shadow renderer for no visual difference.
        Sprite current = playerSpriteRenderer.sprite;
        if (shadowRenderer.sprite != current)
            shadowRenderer.sprite = current;

        if (shadowRenderer.flipX != playerSpriteRenderer.flipX)
            shadowRenderer.flipX = playerSpriteRenderer.flipX;

        if (shadowRenderer.flipY != playerSpriteRenderer.flipY)
            shadowRenderer.flipY = playerSpriteRenderer.flipY;
    }

    private void SetShadowActive(bool active)
    {
        if (shadowRenderer.enabled != active)
            shadowRenderer.enabled = active;
    }
}

