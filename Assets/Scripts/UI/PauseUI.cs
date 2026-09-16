using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

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
    [SerializeField] private TMP_Text levelLabel;
    [SerializeField] private TMP_Text endText;
    [SerializeField] private string congratsText = "Your did it!";
    [SerializeField] private string deathText = "Dead.";
    [SerializeField] private Color congratsColor = Color.green;
    [SerializeField] private Color deathColor = Color.red;

    [SerializeField] private Button nextLevelButton;
    [SerializeField] private Button replayButton;
    [SerializeField] private GameObject[] collectableSlotsFilled;
    [SerializeField] private SettingsUI settingsUI; 
    [SerializeField] private float fadeDuration = 0.3f;

    private PauseContext currentContext;

    // First terminal outcome (win or loss) is authoritative; later terminal triggers from
    // in-flight tweens or mine blasts are ignored. Reset by LevelManager on level (re)start.
    private static bool outcomeResolved;
    public static bool OutcomeResolved => outcomeResolved;

    // Set the instant a death is locked in, cleared once its presentation has been shown.
    // Lets a killing move's own animation (walk-in, catch slide, blast) finish playing
    // before the player is hidden, its corpse spawned, and the pause screen opens.
    private static bool gameOverAwaitingPresentation;
    public static bool IsGameOverAwaitingPresentation => gameOverAwaitingPresentation;

    public static void ResetOutcome()
    {
        outcomeResolved = false;
        gameOverAwaitingPresentation = false;
    }

    // Claims the death for this turn. Returns true only for the first caller; a hazard
    // that resolves later in the same turn gets false back and must skip its own kill
    // presentation entirely, since another cause already won the race.
    public static bool TryLockGameOver()
    {
        if (outcomeResolved) return false;
        outcomeResolved = true;
        gameOverAwaitingPresentation = true;
        return true;
    }

    // Called once the killing move's own animation has fully played out. Hides the
    // player, spawns its corpse, and opens the Game Over pause screen.
    public static void ShowGameOver()
    {
        if (!gameOverAwaitingPresentation) return;
        gameOverAwaitingPresentation = false;
        CombatEvents.RaisePlayerDefeated();
        PresentGameOverScreen();
    }

    // Presents the player's defeat (hide + corpse) right away, but defers opening the
    // pause screen by delay. Used when the killing blow has its own instant visual (a
    // mine's blast) that the corpse should appear alongside, while the screen still
    // waits so it doesn't cut off the explosion FX mid-play.
    public static void ShowGameOverImmediateDefeat(float screenDelay)
    {
        if (!gameOverAwaitingPresentation) return;
        gameOverAwaitingPresentation = false;
        CombatEvents.RaisePlayerDefeated();
        Instance.StartCoroutine(DelayedPresentScreen(screenDelay));
    }

    private static void PresentGameOverScreen()
    {
        Instance.Configure(PauseContext.GameOver);
        UINavigator.Instance.Push(Instance);
        GameStateManager.Instance.ChangeState(GameState.PauseScreen);
    }

    private static IEnumerator DelayedPresentScreen(float delay)
    {
        yield return new WaitForSeconds(delay);
        PresentGameOverScreen();
    }

    private CanvasGroup panelCanvasGroup;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        panelCanvasGroup = panel.GetComponent<CanvasGroup>();
    }

    public void Configure(PauseContext context)
    {
        currentContext = context;
    }

    public override void Show()
    {
        // fade in, make a short delay where it's not interactable while fades in.
        panel.SetActive(true);
        panelCanvasGroup.alpha = 0f;
        panelCanvasGroup.interactable = false;
        panelCanvasGroup.blocksRaycasts = true;

        string worldName = LevelManager.Instance.CurrentWorld.worldName;
        int levelNum = LevelManager.Instance.CurrentLevelIndex + 1;
        levelLabel.text = $"{worldName} - Level {levelNum}";
        
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        // endText only shows in some of the pause Contexts
        endText.enabled = false;
        FillCollectableSlots(CollectableManager.Instance.foundCollectables);
        
        switch (currentContext)
        {
            case PauseContext.LevelComplete:   
                endText.text = congratsText;
                endText.color = congratsColor;
                endText.enabled = true;
                EventSystem.current.SetSelectedGameObject(nextLevelButton.gameObject);
                break;

            case PauseContext.ManualPause:
                EventSystem.current.SetSelectedGameObject(replayButton.gameObject);
                break;

            case PauseContext.GameOver:
                endText.text = deathText;
                endText.color = deathColor;
                endText.enabled = true;
                EventSystem.current.SetSelectedGameObject(replayButton.gameObject);
                break;
        }

        // DOTween and upon completion, set interactable.
        panelCanvasGroup.DOFade(1f, fadeDuration).SetUpdate(true)
            .OnComplete(() =>
            {
                panelCanvasGroup.interactable = true;
            });
    }

    public override void Hide()
    {
        base.Hide();
        ResetCollectableSlots();
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
        LevelManager.Instance.LoadWorldMap();
    }

    public static void Trigger(PauseContext context)
    {
        // Game Over always goes through the lock/present split so a killing move's own
        // animation gets a chance to play first. Any caller that still reaches for
        // Trigger(GameOver) directly (rather than TryLockGameOver + ShowGameOver) falls
        // back to presenting immediately.
        if (context == PauseContext.GameOver)
        {
            TryLockGameOver();
            ShowGameOver();
            return;
        }

        if (context == PauseContext.LevelComplete)
        {
            // First terminal outcome wins; ignore later flips from tweens or mine blasts.
            // this ensures that the move that ended the game, is the final move taken. 
            // ie player cannot just outrun a move that would have killed them.
            if (outcomeResolved) return;
            outcomeResolved = true;

            CollectableManager.Instance.SaveBestScore(LevelManager.Instance.CurrentWorldIndex, LevelManager.Instance.CurrentLevelIndex);
        }

        Instance.Configure(context);
        UINavigator.Instance.Push(Instance);
        GameStateManager.Instance.ChangeState(GameState.PauseScreen);
    }
}
