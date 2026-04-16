using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [SerializeField] private string[] levelScenes;
    
    private int currentLevelIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Start by loading the first level
        LoadLevel(0);
    }

    public void ReplayLevel() => LoadLevel(currentLevelIndex);

    public void LoadNextLevel()
    {
        int next = currentLevelIndex + 1;
        if (next < levelScenes.Length)
            LoadLevel(next);
        else
            LoadMainMenu();
    }

    public void LoadMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }

    private void LoadLevel(int index)
    {
        currentLevelIndex = index;
        CollectableManager.Instance.ResetCount();
        TurnManager.Instance.ClearActors();
        SceneManager.LoadScene(levelScenes[index]);
        GameStateManager.Instance.ChangeState(GameState.PlayMode);
    }
}

