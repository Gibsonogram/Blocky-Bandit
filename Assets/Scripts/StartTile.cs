using UnityEngine;

public class StartTile : MonoBehaviour, IGridActor
{
    public static StartTile Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }
    
    public bool OnPlayerMoveInto(Vector2Int direction)
    {
        return false;
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
