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

        if (levelScene.name == "WorldMap")
        {
            // The world map doubles as the title screen: the map is the scene, the main menu
            // is an overlay pushed on top of it.
            GameStateManager.Instance.ChangeState(GameState.Menus);
            MainMenuUI.Open();
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
                    AtmosphereManager.Instance.SetAtmosphere(worlds[w].atmosphereEffectPrefab);
                    return;
                }
            }
        }
    }

    public void LoadMainMenu()
    {
        UINavigator.Instance.ClearAll();
        GameStateManager.Instance.ChangeState(GameState.Menus);

        // The title screen is the world map with the main menu overlaid on it. The map must be
        // the loaded scene first (its navigator and node visuals Awake during the load), so the
        // menu is pushed once the scene has finished loading rather than straight away.
        SceneManager.sceneLoaded -= OnMainMenuWorldMapLoaded;
        SceneManager.sceneLoaded += OnMainMenuWorldMapLoaded;
        SceneManager.LoadScene("WorldMap");
    }

    private void OnMainMenuWorldMapLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "WorldMap")
            return;

        SceneManager.sceneLoaded -= OnMainMenuWorldMapLoaded;
        MainMenuUI.Open();
    }

    public void LoadWorldMap()
    {
        UINavigator.Instance.ClearAll();
        GameStateManager.Instance.ChangeState(GameState.WorldMap);
        SceneManager.LoadScene("WorldMap");
    }

    // Called by WorldMapNavigator when the player accepts a node
    public void SelectWorld(WorldData world)
    {
        if (world == null)
        {
            Debug.LogWarning("LevelManager cannot select a null world.", this);
            return;
        }

        int worldIndex = System.Array.IndexOf(worlds, world);
        if (worldIndex < 0)
        {
            Debug.LogWarning($"LevelManager world list does not contain '{world.name}'.", this);
            return;
        }

        if (LevelSelectUI.Instance == null || UINavigator.Instance == null)
        {
            Debug.LogWarning("LevelManager cannot open level selection because its UI dependencies are unavailable.", this);
            return;
        }

        currentWorldIndex = worldIndex;
        LevelSelectUI.Instance.Configure(world, currentWorldIndex);
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
        // The outgoing scene's actors unregister themselves as they are destroyed; this is
        // just so a level load can never inherit a committed tile from the previous one.
        GridOccupancy.Clear();
        LevelEndUI.ResetOutcome();
        VisionOverlayRenderer.Instance?.Clear();
        GameStateManager.Instance.ChangeState(GameState.PlayMode);
        SceneManager.LoadScene(worlds[worldIndex].levelSceneNames[levelIndex]);
        AtmosphereManager.Instance.SetAtmosphere(worlds[worldIndex].atmosphereEffectPrefab);
    }

    public void ReplayLevel() => LoadLevel(currentWorldIndex, currentLevelIndex);

    // True when the current world has a level before this one. The end screen greys out
    // Previous at a world's first level instead of quietly sending the player somewhere
    // else; the world map is still one button away (Quit to World).
    public bool HasPreviousLevel => currentLevelIndex > 0;

    public void LoadPreviousLevel()
    {
        if (!HasPreviousLevel)
            return;

        LoadLevel(currentWorldIndex, currentLevelIndex - 1);
    }

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

