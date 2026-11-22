using UnityEngine;
using UnityEngine.Tilemaps;

// Shared plumbing for the silhouettes that stay visible when a sprite is covered by walk-behind
// art. The walk-behind tilemap renders with Custom/WalkBehindStencil, which marks stencil bit
// 128 under the pixels its art actually paints; a second SpriteRenderer using
// Custom/PlayerShadow only survives where that mark exists. So the silhouette shows up exactly
// where the art covers the sprite it mirrors, and nowhere else.
//
// Both the player's shadow and the collectable's outline use this, so the mask material is
// installed once per level no matter how many shadows exist, and the two look identical.
public static class WalkBehindShadow
{
    public const string MaskMaterialResource = "WalkBehindStencil";
    public const string ShadowMaterialResource = "PlayerShadow";

    // Sorting layer the walk-behind art (and every silhouette drawn over it) lives on. Used
    // only when a level has no walk-behind tilemap to read the actual layer/order from.
    private const string WalkBehindSortingLayerName = "WalkBehind";

    // Resolves the sorting for a copy that has to read over the thing covering it - the
    // player's shadow, and the collectable's actor-cover copy. It sits directly above the
    // walk-behind art, which is itself above the actor (Collision) sorting layer.
    public static bool TryResolveCoverSorting(out int sortingLayerId, out int sortingOrder)
    {
        TilemapRenderer tilemapRenderer = WalkBehindRenderer();
        if (tilemapRenderer != null)
        {
            sortingLayerId = tilemapRenderer.sortingLayerID;
            sortingOrder = tilemapRenderer.sortingOrder + 1;
            return true;
        }

        int layerId = SortingLayer.NameToID(WalkBehindSortingLayerName);
        sortingLayerId = layerId;
        sortingOrder = 1;
        // NameToID returns Default's id (0) for a name that does not exist, and there is no
        // layer above the actors to draw on in that case.
        return layerId != 0;
    }

    private static TilemapRenderer WalkBehindRenderer()
    {
        if (GridSettings.Instance == null)
            return null;

        Tilemap walkBehind = GridSettings.WalkBehindTilemap;
        return walkBehind != null ? walkBehind.GetComponent<TilemapRenderer>() : null;
    }

    // Installs the stencil material on the walk-behind tilemap and points 'shadowRenderer' at
    // the matching shadow material, sorted directly above the walk-behind art. Returns false
    // when the level has no walk-behind tilemap or the resources are missing; the caller must
    // keep the renderer disabled in that case, because an unmasked silhouette would cover the
    // whole sprite instead of only the covered pixels.
    //
    // Idempotent: the player shadow and every collectable call it, and the tilemap simply keeps
    // the one shared material.
    public static bool TryInstall(SpriteRenderer shadowRenderer)
    {
        if (shadowRenderer == null || GridSettings.Instance == null)
            return false;

        Material maskMaterial = Resources.Load<Material>(MaskMaterialResource);
        Material shadowMaterial = Resources.Load<Material>(ShadowMaterialResource);
        TilemapRenderer tilemapRenderer = WalkBehindRenderer();

        if (maskMaterial == null || shadowMaterial == null || tilemapRenderer == null)
            return false;

        // Swapping the shared material is harmless when another shadow already did it, and it
        // repairs the case where the tilemap was still using its authored sprite material.
        if (tilemapRenderer.sharedMaterial != maskMaterial)
            tilemapRenderer.sharedMaterial = maskMaterial;

        shadowRenderer.sharedMaterial = shadowMaterial;

        // Sit directly above the walk-behind art so the masked pixels land on top of it.
        shadowRenderer.sortingLayerID = tilemapRenderer.sortingLayerID;
        shadowRenderer.sortingOrder = tilemapRenderer.sortingOrder + 1;
        shadowRenderer.enabled = true;
        return true;
    }

    // Copies the frame the source renderer is showing, so a silhouette follows the same
    // animation. Assigns only when a value actually changed: re-writing the same sprite every
    // frame dirties the shadow renderer for no visual difference. The silhouette keeps its own
    // colour (the dark shadow tint), so only sprite and flips are mirrored.
    public static void Mirror(SpriteRenderer source, SpriteRenderer shadow)
    {
        if (shadow == null)
            return;

        // The source is destroyed on defeat while its shadow's parent can survive.
        if (source == null)
        {
            SetEnabled(shadow, false);
            return;
        }

        bool sourceVisible = source.enabled && source.gameObject.activeInHierarchy;
        SetEnabled(shadow, sourceVisible);
        if (!sourceVisible)
            return;

        Sprite current = source.sprite;
        if (shadow.sprite != current)
            shadow.sprite = current;

        if (shadow.flipX != source.flipX)
            shadow.flipX = source.flipX;

        if (shadow.flipY != source.flipY)
            shadow.flipY = source.flipY;
    }

    private static void SetEnabled(SpriteRenderer renderer, bool enabled)
    {
        if (renderer.enabled != enabled)
            renderer.enabled = enabled;
    }
}
