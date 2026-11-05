using System.Collections;
using System.Collections.Generic;
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

    // The slot visuals authored in the scene for one preset (currently the 3-collectable
    // layout). Their spacing, centre and raised row are read back at Awake and reused as
    // the guide for every other preset, so designers can keep tweaking the padding in the
    // scene without touching this script. Extra slots are cloned from these when a level
    // holds more collectables than were authored.
    [SerializeField] private GameObject[] collectableSlotsFilled;
    [SerializeField] private SettingsUI settingsUI; 
    [SerializeField] private float fadeDuration = 0.3f;

    // How long the player's defeat visual (corpse, and the blast/fall FX that produced
    // it) stays on screen after a kill before the Game Over panel opens. Applies to
    // catches and other move-based kills; the mine overrides it with its own
    // explosion FX length via ShowGameOverImmediateDefeat.
    [SerializeField] private float deathScreenDelay = 0.5f;

    private PauseContext currentContext;

    // Live copies of the collectable slot visuals. Seeded from collectableSlotsFilled and
    // grown by cloning the authored slots when a level has more collectables than the
    // authored preset (e.g. a 4-collectable level against the authored 3-slot row).
    private readonly List<GameObject> runtimeCollectableSlots = new List<GameObject>();
    private Transform collectablesRow;
    private float collectableSlotCentreX;
    private float collectableSlotBaseY;
    private float collectableSlotPitch;
    private float collectableSlotRaiseY;
    private bool collectableLayoutCached;

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
    // player and spawns its corpse right away, then holds the pause screen back by
    // deathScreenDelay so the corpse reads on screen before the panel covers it — the
    // same courtesy the mine's blast already gets. Without the hold the screen opened on
    // the same frame as the corpse, so a rook/bishop catch looked like it skipped
    // straight to Game Over.
    public static void ShowGameOver() => ShowGameOverImmediateDefeat(Instance.deathScreenDelay);

    // Presents the player's defeat (hide + corpse) right away, but defers opening the
    // pause screen by screenDelay so the defeat visual gets a beat on screen first. The
    // mine passes its explosion FX length so the blast isn't cut off mid-play;
    // ShowGameOver() passes deathScreenDelay for move-based kills (rook/bishop catch,
    // walking into a hazard, a hole fall).
    public static void ShowGameOverImmediateDefeat(float screenDelay)
    {
        if (!gameOverAwaitingPresentation) return;
        gameOverAwaitingPresentation = false;
        CombatEvents.RaisePlayerDefeated();
        Instance.StartCoroutine(DelayedPresentScreen(screenDelay));
    }

    // Schedules the player's defeat presentation to begin once the killing move's own
    // animation has landed. Runs on this persistent object rather than on the killing
    // actor: if that actor is destroyed later in the same turn — a mine blast resolving
    // after it, or a collision cull — its own coroutine dies with it and the Game Over it
    // had already locked in would never be presented. The lock still admits only the
    // first caller, so exactly one presentation still happens.
    public static void ScheduleGameOverAfter(float delay)
    {
        if (!gameOverAwaitingPresentation) return;
        Instance.StartCoroutine(DelayedShowGameOver(delay));
    }

    private static IEnumerator DelayedShowGameOver(float delay)
    {
        yield return new WaitForSeconds(delay);
        ShowGameOver();
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
        CacheCollectableSlotLayout();
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
        FillCollectableSlots(ResolveCollectableSlotCount(), ResolveFoundCollectables());
        
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

    // Reads the authored preset back so every other arrangement inherits its padding: the
    // horizontal pitch between slots, the centre of the row, the baseline height, and how far
    // the raised slot sits above that baseline.
    private void CacheCollectableSlotLayout()
    {
        runtimeCollectableSlots.Clear();
        if (collectableSlotsFilled == null)
            return;

        foreach (GameObject slot in collectableSlotsFilled)
            if (slot != null)
                runtimeCollectableSlots.Add(slot);

        if (runtimeCollectableSlots.Count == 0)
            return;

        collectablesRow = runtimeCollectableSlots[0].transform.parent;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        int measured = 0;

        foreach (GameObject slot in runtimeCollectableSlots)
        {
            if (!(slot.transform is RectTransform rect))
                continue;

            Vector2 position = rect.anchoredPosition;
            minX = Mathf.Min(minX, position.x);
            maxX = Mathf.Max(maxX, position.x);
            minY = Mathf.Min(minY, position.y);
            maxY = Mathf.Max(maxY, position.y);
            measured++;
        }

        if (measured < 2)
            return;

        collectableSlotCentreX = (minX + maxX) * 0.5f;
        collectableSlotPitch = (maxX - minX) / (measured - 1);
        collectableSlotBaseY = minY;
        collectableSlotRaiseY = Mathf.Max(0f, maxY - minY);
        collectableLayoutCached = collectableSlotPitch > 0f;
    }

    // Clones the authored slot visuals until there is one per collectable in the level. The
    // clones live under the same collectables row, so they inherit its scale and the existing
    // art (SlotEmpty shadow plus SlotFilled highlight).
    private void EnsureCollectableSlotCount(int slotCount)
    {
        if (collectablesRow == null || runtimeCollectableSlots.Count == 0)
            return;

        while (runtimeCollectableSlots.Count < slotCount)
        {
            GameObject template = runtimeCollectableSlots[runtimeCollectableSlots.Count - 1];
            if (template == null)
                return;

            GameObject clone = Instantiate(template, collectablesRow);
            clone.name = $"Collectible{runtimeCollectableSlots.Count + 1}";
            runtimeCollectableSlots.Add(clone);
        }
    }

    // Arranges the visible slots on one row using the authored pitch, keeping the group
    // centred: 1 sits centred, 2 sit side by side, 3 keep the authored middle-raised preset,
    // and 4 raise the two middle slots with one lower on each side. Wider levels keep the same
    // pitch and lift their centre slot(s) too.
    private void LayoutCollectableSlots(int slotCount)
    {
        if (!collectableLayoutCached)
        {
            // Nothing authored to copy the spacing from: just show the slots the level needs.
            for (int i = 0; i < runtimeCollectableSlots.Count; i++)
                if (runtimeCollectableSlots[i] != null)
                    runtimeCollectableSlots[i].SetActive(i < slotCount);
            return;
        }

        for (int i = 0; i < runtimeCollectableSlots.Count; i++)
        {
            GameObject slot = runtimeCollectableSlots[i];
            if (slot == null)
                continue;

            bool visible = i < slotCount;
            slot.SetActive(visible);
            if (!visible || !(slot.transform is RectTransform rect))
                continue;

            float offsetFromCentre = i - (slotCount - 1) * 0.5f;
            float x = collectableSlotCentreX + offsetFromCentre * collectableSlotPitch;
            float y = collectableSlotBaseY + (IsRaisedSlot(i, slotCount) ? collectableSlotRaiseY : 0f);
            rect.anchoredPosition = new Vector2(x, y);
        }
    }

    private static bool IsRaisedSlot(int index, int slotCount)
    {
        // A single slot sits on its own and two sit side by side, so neither preset has a
        // centre slot to lift. From three upwards the middle slot (or the middle pair) rises,
        // matching the authored 3-collectable preset.
        if (slotCount < 3)
            return false;

        if (slotCount % 2 == 1)
            return index == slotCount / 2;

        return index == slotCount / 2 - 1 || index == slotCount / 2;
    }

    // The pause screen shows one empty space per collectable the level actually contains, so a
    // level authored with 1, 2, 3 or 4 collectables gets its own arrangement. The count comes
    // from the collectables that registered themselves this run (CollectableManager) rather
    // than a constant, so placing a fourth collectable in a scene is all it takes.
    private static int ResolveCollectableSlotCount()
    {
        return CollectableManager.Instance != null ? Mathf.Max(0, CollectableManager.Instance.totalCollectables) : 0;
    }

    private static int ResolveFoundCollectables()
    {
        return CollectableManager.Instance != null ? Mathf.Max(0, CollectableManager.Instance.foundCollectables) : 0;
    }

    private void FillCollectableSlots(int slotCount, int found)
    {
        EnsureCollectableSlotCount(slotCount);
        LayoutCollectableSlots(slotCount);

        for (int i = 0; i < runtimeCollectableSlots.Count; i++)
        {
            GameObject slot = runtimeCollectableSlots[i];
            if (slot == null)
                continue;

            Transform filled = slot.transform.Find("SlotFilled");
            if (filled != null)
                filled.gameObject.SetActive(i < found && i < slotCount);
        }
    }

    private void ResetCollectableSlots()
    {
        foreach (GameObject slot in runtimeCollectableSlots)
        {
            if (slot == null)
                continue;

            Transform filled = slot.transform.Find("SlotFilled");
            if (filled != null)
                filled.gameObject.SetActive(false);
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
