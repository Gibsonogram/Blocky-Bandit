using UnityEngine;

// Manages the world map screen. Attach to a persistent WorldMapManager GameObject in WorldMap.unity.
// World nodes call SelectWorld() when the player confirms on them.
// The level select panel is shown/hidden based on GameState.WorldLevelSelect.
public class WorldMapManager : MonoBehaviour
{
    [SerializeField] private WorldLevelSelectUI levelSelectUI;

    private void Start()
    {
        GameStateManager.Instance.OnStateChanged += OnStateChanged;
        levelSelectUI.Hide();
    }

    private void OnDestroy()
    {
        GameStateManager.Instance.OnStateChanged -= OnStateChanged;
    }

    void OnStateChanged(GameState newState)
    {
        if (newState == GameState.WorldLevelSelect)
            levelSelectUI.Show(LevelManager.Instance.CurrentWorld, LevelManager.Instance.CurrentWorldIndex);
        else
            levelSelectUI.Hide();
    }

    // Called by a world node when the player confirms on it
    public void SelectWorld(int worldIndex)
    {
        LevelManager.Instance.SelectWorld(worldIndex);
    }
}
