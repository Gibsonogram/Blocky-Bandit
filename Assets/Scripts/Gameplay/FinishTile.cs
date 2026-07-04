using UnityEngine;

public class FinishTile : MonoBehaviour, IGridActor
{
    public bool OnPlayerMoveInto(Vector2Int direction)
    {
        // Don't declare the win here: the enemies still take their turn this same frame
        // and can catch the player as they reach the exit. TurnManager resolves the win
        // at the end of the turn, only if the player survives (death takes priority).
        TurnManager.Instance.FlagPlayerReachedFinish();
        return true;
    }
}
