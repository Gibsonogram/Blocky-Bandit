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
// returning a fresh list keeps the once-per-turn refresh allocation-free, and each push becomes
// one indicator the overlay draws. Two actors covering the same tile therefore produce two
// indicators rather than one, which is how the player sees that both are watching it.
public interface IVisionTileSink
{
    void Add(Vector2Int tile);
}

public interface IVisionSource
{
    VisionSeverity Severity { get; }

    // Shape this actor draws for every tile it covers. Overlapping actors keep their own shape,
    // so the outlines of both are plotted on the shared tile.
    VisionOverlayShape Shape { get; }

    void CollectDangerTiles(IVisionTileSink sink);
}
