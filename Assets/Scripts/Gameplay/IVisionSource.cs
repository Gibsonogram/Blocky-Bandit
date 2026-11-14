using UnityEngine;

// How a source's danger footprint should be presented. Warning is the "this will arm a mine,
// start a chase, or fire on a later turn" colour; Lethal is the "standing here gets you killed
// on the actor's next turn" colour. The rules that choose between them live in each actor's
// state, not in the overlay.
public enum VisionSeverity
{
    Warning,
    Lethal,
}

// Receives a source's danger tiles as it walks them. Pushing tiles into the overlay instead of
// returning a fresh list keeps the once-per-turn refresh allocation-free: the overlay counts
// each tile as it arrives, so overlapping cones are known without any extra pass.
public interface IVisionTileSink
{
    void Add(Vector2Int tile);
}

public interface IVisionSource
{
    VisionSeverity Severity { get; }
    void CollectDangerTiles(IVisionTileSink sink);
}
