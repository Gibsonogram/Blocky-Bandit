using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Collectable : MonoBehaviour, IGridActor
{
    [SerializeField] private float animDuration = 0.25f;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite[] idleFrames;
    [SerializeField] private int frameIndex;

    // Floor pickups: above the walk-in-front art (WalkInFront/0) but below the danger overlay,
    // which VisionOverlayStyle draws on WalkInFront/5, so a warning marker stays readable on
    // top of a gem. Actors are never on this layer - ActorSorting puts them on Collision, which
    // draws after WalkInFront - so every actor still renders over a collectable.
    // Set here instead of in the prefab so every Collectable variant follows the same stack.
    private const string SortingLayerName = "WalkInFront";
    private const int SortingOrder = 1;

    // The silhouette shown wherever the gem is covered: the walk-behind art copy is stencil
    // masked by the tilemap art, and the actor-cover copy is shown while another actor's body
    // renders over the gem. Defaults to the player shadow's 62% black so the systems read as
    // one.
    [SerializeField] private Color shadowTint = new Color(0f, 0f, 0f, 0.627451f);
    private SpriteRenderer shadowRenderer;
    private SpriteRenderer coverShadowRenderer;

    // Reused by IsCoveredByActor. A point query is the tile test: every actor collider covers
    // its own tile's centre, while a collider on a neighbouring tile does not reach it.
    private static readonly List<Collider2D> ActorHits = new();
    private ContactFilter2D actorFilter;

    private void Awake()
    {
        if (spriteRenderer == null)
            return;

        spriteRenderer.sortingLayerName = SortingLayerName;
        spriteRenderer.sortingOrder = SortingOrder;

        if (GridSettings.Instance != null)
        {
            actorFilter = new ContactFilter2D();
            actorFilter.useTriggers = Physics2D.queriesHitTriggers;
            actorFilter.SetLayerMask(GridSettings.ActorLayer);
        }

        CreateShadow();
        CreateCoverShadow();
    }

    // A second SpriteRenderer on a child object, masked to the walk-behind art and sorted right
    // above it. It is created at runtime so every Collectable prefab gets the outline without
    // being re-authored, and it mirrors the gem's own frame (see LateUpdate) so it runs the
    // same idle animation while it is hidden under the art.
    private void CreateShadow()
    {
        GameObject shadowObject = new GameObject("Shadow");
        shadowObject.transform.SetParent(transform, false);

        shadowRenderer = shadowObject.AddComponent<SpriteRenderer>();
        shadowRenderer.color = shadowTint;
        shadowRenderer.sprite = spriteRenderer.sprite;

        if (!WalkBehindShadow.TryInstall(shadowRenderer))
        {
            // No mask: an unmasked silhouette would cover the gem instead of outline it.
            shadowRenderer = null;
            Destroy(shadowObject);
        }
    }

    // A third copy of the gem, NOT stencil masked, drawn on the silhouette stacking (directly
    // above the actor layer). It is hidden unless another actor is standing on the gem's tile:
    // actor sprites live on the Collision sorting layer, above the gem's WalkInFront layer, so
    // a crate or enemy (they can commit onto a collectable's tile) would otherwise hide it
    // completely. This is what keeps the gem readable while it is covered.
    private void CreateCoverShadow()
    {
        GameObject shadowObject = new GameObject("CoverShadow");
        shadowObject.transform.SetParent(transform, false);

        coverShadowRenderer = shadowObject.AddComponent<SpriteRenderer>();
        coverShadowRenderer.sprite = spriteRenderer.sprite;
        coverShadowRenderer.color = shadowTint;
        // Same material as the gem itself (just tinted dark), so nothing extra is needed;
        // only the sorting puts this copy over the actor that covers it.
        coverShadowRenderer.sharedMaterial = spriteRenderer.sharedMaterial;

        if (!WalkBehindShadow.TryResolveCoverSorting(out int layerId, out int order))
        {
            coverShadowRenderer = null;
            Destroy(shadowObject);
            return;
        }

        coverShadowRenderer.sortingLayerID = layerId;
        coverShadowRenderer.sortingOrder = order;
        coverShadowRenderer.enabled = false;
    }

    // Late update so the gem's own Update has already chosen this frame's idle sprite, which
    // keeps both outlines on exactly the same frame.
    private void LateUpdate()
    {
        bool sourceVisible = spriteRenderer != null &&
                             spriteRenderer.enabled &&
                             spriteRenderer.gameObject.activeInHierarchy;

        if (shadowRenderer != null)
            WalkBehindShadow.Mirror(spriteRenderer, shadowRenderer);

        if (coverShadowRenderer == null)
            return;

        if (!sourceVisible)
        {
            coverShadowRenderer.enabled = false;
            return;
        }

        WalkBehindShadow.Mirror(spriteRenderer, coverShadowRenderer);
        // Mirror follows the gem's visibility; this copy additionally only shows while an
        // actor is actually covering the gem's tile.
        coverShadowRenderer.enabled = IsCoveredByActor();
    }

    // True while another actor covers the gem's tile. Two cheap reads: the committed board
    // catches crates and enemies the instant their move starts (they can commit onto a
    // collectable's tile), and the point query catches anything not on that board (the
    // player). The point is the gem's centre, so a collider on a neighbouring tile does not
    // count as covering it.
    private bool IsCoveredByActor()
    {
        if (GridSettings.Instance == null)
            return false;

        if (GridOccupancy.TryGet(GridUtils.WorldToGrid(transform.position), out Component occupant) &&
            occupant != null)
        {
            return true;
        }

        // Non-alloc query: only the hit count is needed, and the shared list is not kept.
        return Physics2D.OverlapPoint(transform.position, actorFilter, ActorHits) > 0;
    }

    public bool OnPlayerMoveInto(Vector2Int direction)
    {
        // A bunch of housekeeping. set rigidbody to false, play short anim, 
        // increment manager counter, destroy...
        GetComponent<Collider2D>().enabled = false;
        CollectableManager.Instance.RegisterCollection();
        StartCoroutine(TriggerCollection());
        return true;
    }

    IEnumerator TriggerCollection()
    {
        // lerp upward on collect
        Vector3 startPos = transform.position;
        Vector3 targetPos = startPos + new Vector3(0f, 0.75f, 0f);
        float elapsed = 0f;
        while (elapsed < animDuration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(startPos, targetPos, elapsed / animDuration);
            yield return null;
        }

        Destroy(gameObject);
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        CollectableManager.Instance.RegisterTotal();
    }

    // Update is called once per frame
    void Update()
    {
        // The frame is authored on the prefab (frameIndex never advances), so writing the same
        // sprite every frame only dirties the renderer. Assign only when it actually changes.
        Sprite next = idleFrames[frameIndex];
        if (spriteRenderer.sprite != next)
            spriteRenderer.sprite = next;
    }
}
