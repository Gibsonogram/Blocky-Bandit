using System.Collections.Generic;
using UnityEngine;

// Committed-tile occupancy for gameplay reads.
//
// A movable actor commits its grid position the instant its move starts, but its
// Rigidbody2D — and therefore every Physics2D query — only arrives when the move tween
// lands, ~0.15s later. Enemy and hazard logic must read the committed board instead, or it
// assesses the pre-move board: a bishop slides through (or is blocked by) a crate that has
// just moved, and a mine only notices an actor that walked into its cone one turn late.
//
// Static content (the collision tilemap), the player and anything else without a committed
// tile still resolve through GridUtils/Physics2D, exactly as before.
public static class GridOccupancy
{
    private static readonly Dictionary<Vector2Int, Component> committed = new();
    // Every component that holds (or has held) a committed tile. Movers are authoritative
    // at their committed tile only, so the physics fallback can tell a mover's lagging
    // collider apart from static content.
    private static readonly HashSet<Component> movers = new();
    // Reused hit buffer for the physics fallback, so a blast sweeping hundreds of tiles does
    // not allocate a Collider2D array per tile.
    private static readonly List<Collider2D> hits = new();

    public static void Clear()
    {
        committed.Clear();
        movers.Clear();
    }

    // Called from Awake, before any Start() runs, so a neighbouring mine's baseline hash
    // (or any other logical read) sees this actor at its real tile rather than at the pose
    // its collider happened to be authored at.
    public static void Register(Vector2Int tile, Component occupant)
    {
        if (committed.TryGetValue(tile, out Component existing) && existing != null && existing != occupant)
            Debug.LogWarning($"GridOccupancy: {occupant.name} committed to {tile}, already held by {existing.name}.", occupant);

        committed[tile] = occupant;
        movers.Add(occupant);
    }

    // A move commits the destination immediately and frees the tile it left. Deliberately
    // silent, unlike Register: a move can legitimately land on an occupied tile (an enemy
    // reaching the player, two actors converging before ResolveActorCollisions settles them),
    // so only scene-load registrations are worth warning about.
    public static void Move(Vector2Int from, Vector2Int to, Component occupant)
    {
        Unregister(from, occupant);
        committed[to] = occupant;
        movers.Add(occupant);
    }

    // Identity-checked: a stale call (already destroyed, or from another actor that has
    // since taken the tile) never evicts the current occupant.
    public static void Unregister(Vector2Int tile, Component occupant)
    {
        if (committed.TryGetValue(tile, out Component existing) && ReferenceEquals(existing, occupant))
        {
            committed.Remove(tile);
            movers.Remove(occupant);
        }
    }

    public static bool TryGet(Vector2Int tile, out Component occupant) => committed.TryGetValue(tile, out occupant);

    // Committed-tile twin of GridUtils.QueryActorTile — same contract, but movers resolve
    // from their committed tile instead of their lagging collider.
    public static IGridActor QueryActorTile(Vector2Int tile, out bool isHardBlocked)
    {
        if (GridUtils.IsCollisionTile(tile))
        {
            isHardBlocked = true;
            return null;
        }

        if (TryGet(tile, out Component occupant))
        {
            // A committed mover without a grid-actor interface blocks, matching how
            // QueryActorTile treats a collider that carries no IGridActor.
            isHardBlocked = occupant is not IGridActor;
            return occupant as IGridActor;
        }

        return PhysicsFallback(tile, out isHardBlocked);
    }

    // Static content only: the collision tilemap is handled by the caller, and a mover is
    // skipped because its committed tile (looked up above, and missed) is the truth — its
    // collider can still be sliding through the tile it just left, and reporting that would
    // hold the vacated tile for the rest of the tween, which is the "fuse one turn late"
    // failure this class exists to remove.
    private static IGridActor PhysicsFallback(Vector2Int tile, out bool isHardBlocked)
    {
        // Reused hit buffer: this runs for every tile of a committed-tile read, and a blast
        // sweeps a whole row and column, so a fresh array per query was steady GC churn.
        int hitCount = GridUtils.OverlapActorColliders(GridUtils.GridToWorld(tile), hits);
        bool hasNonActorCollider = false;
        for (int i = 0; i < hitCount; i++)
        {
            IGridActor actor = hits[i].GetComponent<IGridActor>();

            if (actor is Component component && movers.Contains(component))
                continue;

            if (actor == null)
            {
                hasNonActorCollider = true;
                continue;
            }

            if (actor is FinishTile)
                continue;

            isHardBlocked = false;
            return actor;
        }

        isHardBlocked = hasNonActorCollider;
        return null;
    }
}