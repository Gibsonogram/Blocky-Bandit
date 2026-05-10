using UnityEngine;

public class FinishTile : MonoBehaviour, IGridActor
{
    public bool OnPlayerMoveInto(Vector2Int direction)
    {
        // finish state.
        GameStateManager.Instance.ChangeState(GameState.EndScreen);
        return true;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
