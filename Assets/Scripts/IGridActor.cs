using UnityEditor.AdaptivePerformance.Editor;
using UnityEngine;

public interface IGridActor
{
    bool OnPlayerMoveInto(Vector2Int direction);
}
