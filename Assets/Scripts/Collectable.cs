using UnityEngine;

public class Collectable : MonoBehaviour, IGridActor
{

    public bool OnPlayerMoveInto(Vector2Int direction)
    {
        // the goal is to get the sprite to disappear when player hits this tile.
        // we do a bunch of housekeeping and then we destroy.
        //StartCoroutine(TriggerCollect());
        Destroy(gameObject);
        return false;
    }

    void TriggerCollect()
    {
        
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
