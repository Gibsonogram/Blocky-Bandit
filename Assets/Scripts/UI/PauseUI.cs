using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The manual pause menu: resume, restart, options and a way out to the world map. It is a
// deliberate escape hatch, not a debrief, so it carries none of the end-of-level state
// (collectables, result text, next level). Those live on LevelEndUI, which opens only when
// an outcome has been decided and cannot be dismissed with Back.
//
// Like every other menu it is authored inside the persistent GameUI canvas (Screen Space -
// Overlay, sized by UIViewportFitter), so text renders at native resolution and the panel is
// untouched by the Pixel Perfect Camera behind it.
public class PauseUI : UIScreen
{
    public static PauseUI Instance { get; private set; }

    [Header("Layout")]
    [SerializeField] private TMP_Text levelLabel;
    [SerializeField] private float fadeDuration = 0.3f;

    [Header("Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button replayButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;

    private CanvasGroup panelCanvasGroup;
    private Tween fadeTween;

    // True from the moment the pause screen is pushed until it is popped, whether or not
    // another screen (Options) is stacked on top of it. The pause key doubles as the
    // resume key: while Options is open it closes Options first, then the pause screen.
    public static bool IsPaused =>
        Instance != null && UINavigator.Instance != null && UINavigator.Instance.Contains(Instance);

    public override bool CanGoBack => true;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (panel != null)
            panelCanvasGroup = panel.GetComponent<CanvasGroup>();
    }

    // Opens the manual pause. TurnManager calls this only while playing and only before an
    // outcome is locked in, so there is no context to configure here.
    public static void Open()
    {
        if (Instance == null)
        {
            Debug.LogWarning("PauseUI cannot open: no PauseUI screen exists in the Persistent scene.");
            return;
        }

        UINavigator.Instance.Push(Instance);
        GameStateManager.Instance.ChangeState(GameState.PauseScreen);
    }

    public override void Show()
    {
        panel.SetActive(true);

        // Fade in, and stay non-interactable until the fade finishes so a click that was
        // already travelling cannot land on a button the player has not seen yet.
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = true;
        }

        if (levelLabel != null && LevelManager.Instance != null)
        {
            string worldName = LevelManager.Instance.CurrentWorld.worldName;
            int levelNum = LevelManager.Instance.CurrentLevelIndex + 1;
            levelLabel.text = $"{worldName} - Level {levelNum}";
        }

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        SelectDefaultButton();

        fadeTween?.Kill();
        if (panelCanvasGroup != null)
        {
            fadeTween = panelCanvasGroup.DOFade(1f, fadeDuration).SetUpdate(true)
                .OnComplete(() =>
                {
                    if (panelCanvasGroup != null)
                        panelCanvasGroup.interactable = true;
                });
        }
    }

    public override void Hide()
    {
        fadeTween?.Kill();
        fadeTween = null;

        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
        }

        base.Hide();
    }

    private void SelectDefaultButton()
    {
        if (EventSystem.current == null)
            return;

        Button defaultButton = resumeButton != null ? resumeButton
            : replayButton != null ? replayButton
            : settingsButton != null ? settingsButton
            : quitButton;

        if (defaultButton != null)
            EventSystem.current.SetSelectedGameObject(defaultButton.gameObject);
    }

    // Resume is the same back-step the pause key takes: it closes Options first if that is
    // stacked on top, then this screen, leaving the navigator empty and the game in PlayMode.
    public void OnResume() => UINavigator.Instance.OnBack();

    public void OnReplay() => LevelManager.Instance.ReplayLevel();

    public void OnSettings()
    {
        if (SettingsUI.Instance == null)
        {
            Debug.LogWarning("PauseUI cannot open options because SettingsUI is missing.", this);
            return;
        }

        UINavigator.Instance.Push(SettingsUI.Instance);
    }

    public void OnQuit() => LevelManager.Instance.LoadWorldMap();
}
