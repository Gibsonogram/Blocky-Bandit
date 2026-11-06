using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

// Top-down sprites have to read on top of the collision tilemap, otherwise a wall swallows an
// actor that is stepping past it. The exceptions all come from where the actor's art overlaps a
// wall tile:
//   - a straight-down step, including the whole animation of a downward bump, passes behind;
//   - a bump into a wall beside the actor (left/right) or below it also passes behind, because
//     the bump slides the sprite into that wall's tile;
//   - everything else stays in front: stepping up to a higher Y, a sideways slide, and every
//     bishop diagonal (a diagonal slide must never be swallowed by a wall corner it passes).
//
// The component attaches itself to every Rigidbody2D when a scene loads, so the scene-authored
// player and the prefab actors all follow the same rule without per-prefab or per-scene setup.
[DisallowMultipleComponent]
public class ActorSorting : MonoBehaviour
{
    // The collision tilemap renders on this layer; actors join it and sort just above the tiles.
    private const string AboveCollisionLayerName = "Collision";
    // Layer that renders directly behind the collision tilemap: the "stepped behind" state.
    private const string BehindCollisionLayerName = "player";

    // The player stays one step below the other actors so a catching enemy still reads on top
    // of it, which is how the old dedicated 'player' sorting layer behaved.
    private const int PlayerAboveOffset = 1;
    private const int ActorAboveOffset = 2;

    private readonly List<Renderer> actorRenderers = new List<Renderer>();
    private int aboveOrder;
    private bool isBeneath;
    private bool hasApplied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        for (int i = 0; i < SceneManager.sceneCount; i++)
            AttachToActors(SceneManager.GetSceneAt(i));
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachToActors(scene);

    private static void AttachToActors(Scene scene)
    {
        if (!scene.isLoaded)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Rigidbody2D body in root.GetComponentsInChildren<Rigidbody2D>(true))
            {
                // Every mover in the project is a kinematic body: the player, enemies and
                // crates. Static bodies are scenery (the collision tilemap keeps one for its
                // composite collider), never actors, and must not be re-sorted.
                if (body.bodyType == RigidbodyType2D.Static)
                    continue;

                if (!body.TryGetComponent<ActorSorting>(out _))
                    body.gameObject.AddComponent<ActorSorting>();
            }
        }
    }

    // Sort for the step as it starts, so the actor is already correct on the frame it sets off.
    public static void BeginStep(GameObject actor, Vector2Int direction)
    {
        if (actor != null && actor.TryGetComponent<ActorSorting>(out ActorSorting sorting))
            sorting.SetBeneath(IsBlockedStepBeneath(direction));
    }

    public static void BeginStep(GameObject actor, Vector3 from, Vector3 to)
    {
        if (actor != null && actor.TryGetComponent<ActorSorting>(out ActorSorting sorting))
            sorting.SetBeneath(IsDirectlyDown(from, to));
    }

    // Restores the normal "on top of the tiles" state once the step has landed.
    public static void EndStep(GameObject actor)
    {
        if (actor != null && actor.TryGetComponent<ActorSorting>(out ActorSorting sorting))
            sorting.SetBeneath(false);
    }

    // A bump swings the sprite a quarter tile into the tile it was blocked by. Only a wall above
    // leaves the actor reading in front of it; a wall beside or below overlaps the sprite, so the
    // actor belongs behind the wall art for the whole bump animation.
    private static bool IsBlockedStepBeneath(Vector2Int direction) => direction != Vector2Int.up;

    private static bool IsDirectlyDown(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        // Only a straight descent drops an actor behind the walls. Stepping up reads in front,
        // and a diagonal step (every bishop move) always stays in front of the tiles it passes,
        // which is why the x test matters.
        return delta.y < 0f && Mathf.Approximately(delta.x, 0f);
    }

    private void Awake()
    {
        int collisionLayer = SortingLayer.NameToID(AboveCollisionLayerName);
        int behindLayer = SortingLayer.NameToID(BehindCollisionLayerName);

        // Sprites plus world-space text meshes (the mine's fuse countdown) so the whole actor
        // moves together. Tilemap renderers and other scenery are never part of an actor.
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is not SpriteRenderer && renderer is not MeshRenderer)
                continue;

            // Only the actor's body: the player's walk-behind silhouette and any child authored
            // on another layer (FX, foreground props) keep their own sorting.
            if (renderer.sortingLayerID == collisionLayer || renderer.sortingLayerID == behindLayer)
                actorRenderers.Add(renderer);
        }

        aboveOrder = ResolveCollisionOrder() +
            (GetComponent<PlayerController>() != null ? PlayerAboveOffset : ActorAboveOffset);
        Apply(goBeneath: false);
    }

    private void SetBeneath(bool value)
    {
        if (hasApplied && isBeneath == value)
            return;

        Apply(value);
    }

    private void Apply(bool goBeneath)
    {
        isBeneath = goBeneath;
        hasApplied = true;

        int layer = SortingLayer.NameToID(goBeneath ? BehindCollisionLayerName : AboveCollisionLayerName);
        int order = goBeneath ? 0 : aboveOrder;

        foreach (Renderer renderer in actorRenderers)
        {
            if (renderer == null)
                continue;

            renderer.sortingLayerID = layer;
            renderer.sortingOrder = order;
        }
    }

    // Sorts relative to the actual wall tiles, so a level that authors a different tilemap
    // order still keeps the actor a step above it.
    private static int ResolveCollisionOrder()
    {
        if (GridSettings.Instance == null || GridSettings.CollisionTilemap == null)
            return 0;

        TilemapRenderer tilemapRenderer = GridSettings.CollisionTilemap.GetComponent<TilemapRenderer>();
        return tilemapRenderer != null ? tilemapRenderer.sortingOrder : 0;
    }
}
