using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [SerializeField] private WorldData[] worlds;

    private int currentWorldIndex;
    private int currentLevelIndex;

    public WorldData CurrentWorld => worlds[currentWorldIndex];
    public int CurrentWorldIndex => currentWorldIndex;
    public int CurrentLevelIndex => currentLevelIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        LoadWorldMap();
    }

    public void LoadWorldMap()
    {
        GameStateManager.Instance.ChangeState(GameState.WorldMap);
        SceneManager.LoadScene("WorldMap");
    }

    public void LoadMainMenu()
    {
        GameStateManager.Instance.ChangeState(GameState.MainMenu);
        SceneManager.LoadScene("MainMenu");
    }

    // Called by WorldMapManager when player selects a world node
    public void SelectWorld(int worldIndex)
    {
        currentWorldIndex = worldIndex;
        GameStateManager.Instance.ChangeState(GameState.WorldLevelSelect);
    }

    // Called by level select UI when player picks a level
    public void LoadLevel(int worldIndex, int levelIndex)
    {
        // Save outgoing level score before indices change
        CollectableManager.Instance.SaveBestScore(currentWorldIndex, currentLevelIndex);
        CollectableManager.Instance.ResetCount();

        currentWorldIndex = worldIndex;
        currentLevelIndex = levelIndex;

        TurnManager.Instance.ClearActors();
        GameStateManager.Instance.ChangeState(GameState.PlayMode);
        SceneManager.LoadScene(worlds[worldIndex].levelSceneNames[levelIndex]);
    }

    public void ReplayLevel() => LoadLevel(currentWorldIndex, currentLevelIndex);

    public void LoadNextLevel()
    {
        int next = currentLevelIndex + 1;
        if (next < worlds[currentWorldIndex].levelSceneNames.Length)
            LoadLevel(currentWorldIndex, next);
        else
            LoadWorldMap();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        var buildScenes = new System.Collections.Generic.HashSet<string>();
        foreach (var scene in UnityEditor.EditorBuildSettings.scenes)
            buildScenes.Add(System.IO.Path.GetFileNameWithoutExtension(scene.path));

        if (worlds == null) return;
        foreach (var world in worlds)
        {
            if (world == null) continue;
            foreach (var sceneName in world.levelSceneNames)
            {
                if (!buildScenes.Contains(sceneName))
                    Debug.LogWarning($"LevelManager: '{sceneName}' is not in Build Settings.");
            }
        }
    }
#endif
}

