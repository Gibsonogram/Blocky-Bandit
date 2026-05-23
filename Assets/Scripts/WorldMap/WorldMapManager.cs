using UnityEngine;

// Manages the world map screen. Pushes WorldSelectUI on load.
// LevelSelectUI is pushed when the player selects a world via LevelManager.SelectWorld.
public class WorldMapManager : MonoBehaviour
{
    public static WorldMapManager Instance { get; private set; }

    [SerializeField] private WorldSelectUI worldSelectUI;
    [SerializeField] private LevelSelectUI levelSelectUI;

    public LevelSelectUI LevelSelectUI => levelSelectUI;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        UINavigator.Instance.ClearAll();
        UINavigator.Instance.Push(worldSelectUI);
    }
}
