using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum PauseContext
{
    ManualPause,
    LevelComplete,
    GameOver
}

public class PauseUI : UIScreen
{
    public static PauseUI Instance { get; private set; }

    [SerializeField] private Button defaultSelectedButton;
    [SerializeField] private GameObject congratsSection;
    [SerializeField] private GameObject[] collectableSlotsFilled;
    [SerializeField] private SettingsUI settingsUI;

    private PauseContext currentContext;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Configure(PauseContext context)
    {
        currentContext = context;
    }

    public override void Show()
    {
        base.Show();

        bool isLevelComplete = currentContext == PauseContext.LevelComplete;
        congratsSection.SetActive(isLevelComplete);

        if (isLevelComplete)
            FillCollectableSlots(CollectableManager.Instance.foundCollectables);

        EventSystem.current.SetSelectedGameObject(defaultSelectedButton.gameObject);
    }

    public override void Hide()
    {
        base.Hide();
        ResetCollectableSlots();
        congratsSection.SetActive(false);
    }

    void FillCollectableSlots(int found)
    {
        ResetCollectableSlots();
        for (int i = 0; i < found && i < collectableSlotsFilled.Length; i++)
            collectableSlotsFilled[i].SetActive(true);
    }

    void ResetCollectableSlots()
    {
        foreach (GameObject slot in collectableSlotsFilled)
            slot.SetActive(false);
    }

    public void OnReplay() => LevelManager.Instance.ReplayLevel();
    public void OnNextLevel() => LevelManager.Instance.LoadNextLevel();
    public void OnSettings() => UINavigator.Instance.Push(settingsUI);

    public void OnQuit()
    {
        UINavigator.Instance.ClearAll();
        GameStateManager.Instance.ChangeState(GameState.Menus);
        UINavigator.Instance.Push(MainMenuUI.Instance);
        UINavigator.Instance.Push(WorldSelectUI.Instance);
    }

    public static void Trigger(PauseContext context)
    {
        if (context == PauseContext.LevelComplete)
            CollectableManager.Instance.SaveBestScore(LevelManager.Instance.CurrentWorldIndex, LevelManager.Instance.CurrentLevelIndex);

        Instance.Configure(context);
        UINavigator.Instance.Push(Instance);
        GameStateManager.Instance.ChangeState(GameState.PauseScreen);
    }
}
