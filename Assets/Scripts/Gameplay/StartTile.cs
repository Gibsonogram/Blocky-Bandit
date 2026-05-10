using UnityEngine;

public class StartTile : MonoBehaviour
{
    public static StartTile Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }
}
