using UnityEngine;

public interface IGridActor
{
    // true if this actor can be displaced and did something... false if blocks movement.
    bool OnPlayerMoveInto(Vector2Int direction);
}
