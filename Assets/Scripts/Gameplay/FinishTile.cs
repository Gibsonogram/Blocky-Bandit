using UnityEngine;

public class FinishTile : MonoBehaviour, IGridActor
{
    public bool OnPlayerMoveInto(Vector2Int direction)
    {
        PauseUI.Trigger(PauseContext.LevelComplete);
        return true;
    }
}
