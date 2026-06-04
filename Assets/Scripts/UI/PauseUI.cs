using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public enum PauseContext
{
    ManualPause,
    LevelComplete,
    GameOver
}

public class PauseUI : UIScreen
{
    public static PauseUI Instance { get; private set; }

    [Header("Background Settings")]
    [SerializeField] private UnityEngine.UI.Image backgroundImage;
    [SerializeField] private Color normalPauseColor = new Color(0f, 0f, 0f, 0.6f); // Semi-transparent black
    [SerializeField] private Color gameOverColor = new Color(0.6f, 0f, 0f, 0.6f);  // Semi-transparent red

    [SerializeField] private TMP_Text levelLabel;
    [SerializeField] private Button nextLevelButton;
    [SerializeField] private Button replayButton;
    [SerializeField] private GameObject congratsText;
    [SerializeField] private GameObject deathText;
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

        string worldName = LevelManager.Instance.CurrentWorld.worldName;
        int levelNum = LevelManager.Instance.CurrentLevelIndex + 1;
        levelLabel.text = $"{worldName} - Level {levelNum}";
        
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        congratsText.SetActive(false);
        deathText.SetActive(false);

        if (backgroundImage != null)
        {
            backgroundImage.color = (currentContext == PauseContext.GameOver) 
                ? gameOverColor 
                : normalPauseColor;
        }

        FillCollectableSlots(CollectableManager.Instance.foundCollectables);
        
        switch (currentContext)
        {
            case PauseContext.LevelComplete:
                congratsText.SetActive(true);
                EventSystem.current.SetSelectedGameObject(nextLevelButton.gameObject);
                break;

            case PauseContext.ManualPause:
                EventSystem.current.SetSelectedGameObject(replayButton.gameObject);
                break;

            case PauseContext.GameOver:
                deathText.SetActive(true);
                EventSystem.current.SetSelectedGameObject(replayButton.gameObject);
                break;
        }
    }

    public override void Hide()
    {
        base.Hide();
        ResetCollectableSlots();
        congratsText.SetActive(false);
        deathText.SetActive(false);
    }

    void FillCollectableSlots(int found)
    {
        // Ensure all parents are active so shadows (SlotEmpty) are visible
        foreach (GameObject slotParent in collectableSlotsFilled)
        {
            slotParent.SetActive(true);
            Transform filled = slotParent.transform.Find("SlotFilled");
            if (filled != null) filled.gameObject.SetActive(false);
        }

        // Enable only the found items
        for (int i = 0; i < found && i < collectableSlotsFilled.Length; i++)
        {
            Transform filled = collectableSlotsFilled[i].transform.Find("SlotFilled");
            if (filled != null) filled.gameObject.SetActive(true);
        }
    }

    void ResetCollectableSlots()
    {
        foreach (GameObject slotParent in collectableSlotsFilled)
        {
            slotParent.SetActive(true); 
            Transform filled = slotParent.transform.Find("SlotFilled");
            if (filled != null) filled.gameObject.SetActive(false);
        }
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
        {
            CollectableManager.Instance.SaveBestScore(LevelManager.Instance.CurrentWorldIndex, LevelManager.Instance.CurrentLevelIndex);
        }

        Instance.Configure(context);
        UINavigator.Instance.Push(Instance);
        GameStateManager.Instance.ChangeState(GameState.PauseScreen);
    }
}
