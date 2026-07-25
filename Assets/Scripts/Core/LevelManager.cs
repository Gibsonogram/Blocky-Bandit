using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [SerializeField] private WorldData[] worlds;

    private int currentWorldIndex;
    private int currentLevelIndex;

    public WorldData[] Worlds => worlds;
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
        if (SceneManager.sceneCount > 1)
        {
            InitializeFromLoadedScene();
            return;
        }
        LoadMainMenu();
    }

    private void InitializeFromLoadedScene()
    {
        Scene persistentScene = SceneManager.GetSceneByName("Persistent");
        Scene levelScene = SceneManager.GetSceneAt(0) == persistentScene ? SceneManager.GetSceneAt(1) : SceneManager.GetSceneAt(0);

        if (levelScene.name == "MainMenu")
        {
            GameStateManager.Instance.ChangeState(GameState.Menus);
            UINavigator.Instance.Push(MainMenuUI.Instance);
            return;
        }

        if (levelScene.name == "WorldMap")
        {
            GameStateManager.Instance.ChangeState(GameState.WorldMap);
            return;
        }

        for (int w = 0; w < worlds.Length; w++)
        {
            for (int l = 0; l < worlds[w].levelSceneNames.Length; l++)
            {
                if (worlds[w].levelSceneNames[l] == levelScene.name)
                {
                    currentWorldIndex = w;
                    currentLevelIndex = l;
                    return;
                }
            }
        }
    }

    public void LoadMainMenu()
    {
        UINavigator.Instance.ClearAll();
        GameStateManager.Instance.ChangeState(GameState.Menus);
        UINavigator.Instance.Push(MainMenuUI.Instance);
        SceneManager.LoadScene("MainMenu");
    }

    public void LoadWorldMap()
    {
        UINavigator.Instance.ClearAll();
        GameStateManager.Instance.ChangeState(GameState.WorldMap);
        SceneManager.LoadScene("WorldMap");
    }

    // Called by WorldMapNavigator when the player accepts a node
    public void SelectWorld(int worldIndex)
    {
        currentWorldIndex = worldIndex;
        LevelSelectUI.Instance.Configure(CurrentWorld, currentWorldIndex);
        UINavigator.Instance.Push(LevelSelectUI.Instance);
    }

    // Called by level select UI when player picks a level
    public void LoadLevel(int worldIndex, int levelIndex)
    {
        CollectableManager.Instance.ResetCount();

        currentWorldIndex = worldIndex;
        currentLevelIndex = levelIndex;

        UINavigator.Instance.ClearAll();
        TurnManager.Instance.ClearActors();
        PauseUI.ResetOutcome();
        VisionOverlayRenderer.Instance?.Clear();
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

