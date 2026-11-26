using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The main menu, authored in the persistent UI canvas and shown over the world map. Opening it
// keeps the map loaded but hands the screen to the menu: WorldMapMenuBackdrop fades the map's
// dressing away and tours the camera, while this screen slides its title in from above and its
// options in from below. Play reverses that and hands control back to the map.
public class MainMenuUI : UIScreen
{
    public static MainMenuUI Instance { get; private set; }

    [Header("Slide-in Animation")]
    [SerializeField] private RectTransform titleHolder;
    [SerializeField] private RectTransform optionsHolder;
    [Tooltip("Design-unit distance the title and options travel. The panel is 1024 units tall.")]
    [SerializeField] private float slideDistance = 1024f;
    [SerializeField] private float slideDuration = 0.9f;

    [Header("Buttons")]
    [SerializeField] private Button defaultSelectedButton;

    private CanvasGroup panelCanvasGroup;
    private Vector2 titleHome;
    private Vector2 optionsHome;
    private Coroutine slideRoutine;
    private WorldMapMenuBackdrop worldMapBackdrop;
    private bool closing;

    // The menu is the root of the menu stack: Back must not dismiss it into the world map.
    public override bool CanGoBack => false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (panel != null)
            panelCanvasGroup = panel.GetComponent<CanvasGroup>();

        if (titleHolder != null)
            titleHome = titleHolder.anchoredPosition;

        if (optionsHolder != null)
            optionsHome = optionsHolder.anchoredPosition;
    }

    // Pushes the menu over whatever scene is loaded. Used at boot (the world map is the only
    // loaded scene) and by the world map's Cancel input.
    public static void Open()
    {
        if (Instance == null)
        {
            Debug.LogWarning("MainMenuUI cannot open: no MainMenuUI screen exists in the Persistent scene.");
            return;
        }

        if (UINavigator.Instance == null || GameStateManager.Instance == null)
            return;

        if (UINavigator.Instance.Contains(Instance))
            return;

        GameStateManager.Instance.ChangeState(GameState.Menus);
        UINavigator.Instance.Push(Instance);
    }

    public override void Show()
    {
        panel.SetActive(true);
        closing = false;

        ResolveWorldMapBackdrop();
        worldMapBackdrop?.EnterMenuMode();

        if (panelCanvasGroup != null)
        {
            // The background is transparent, so the menu itself never fades: the slide is the
            // transition. Interactivity waits until the slide has landed.
            panelCanvasGroup.alpha = 1f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = true;
        }

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        StartSlide(true);
    }

    // Hiding without Play (Options pushed over the menu, or Play finishing its slide) is
    // immediate. The map is only handed back by OnPlay, so the backdrop keeps its state.
    public override void Hide()
    {
        if (!closing)
            StopSlide();

        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
        }

        base.Hide();
    }

    public void OnPlay()
    {
        if (closing)
            return;

        closing = true;

        if (panelCanvasGroup != null)
            panelCanvasGroup.interactable = false;

        worldMapBackdrop?.ExitMenuMode();

        StartSlide(false, () =>
        {
            if (UINavigator.Instance != null)
                UINavigator.Instance.Pop();

            if (GameStateManager.Instance != null)
                GameStateManager.Instance.ChangeState(GameState.WorldMap);

            closing = false;
        });
    }

    public void OnSettings()
    {
        if (SettingsUI.Instance == null)
        {
            Debug.LogWarning("MainMenuUI cannot open options because SettingsUI is missing.", this);
            return;
        }

        UINavigator.Instance.Push(SettingsUI.Instance);
    }

    public void OnQuit() => Application.Quit();

    private void ResolveWorldMapBackdrop()
    {
        if (worldMapBackdrop == null)
            worldMapBackdrop = FindAnyObjectByType<WorldMapMenuBackdrop>();
    }

    private void StartSlide(bool show, System.Action onComplete = null)
    {
        StopSlide();

        // Park the groups off-screen before the first visible frame, so the panel never
        // flashes its final layout for a frame before the slide begins.
        if (show)
            ApplySlide(0f);

        slideRoutine = StartCoroutine(SlideRoutine(show, onComplete));
    }

    private void StopSlide()
    {
        if (slideRoutine != null)
        {
            StopCoroutine(slideRoutine);
            slideRoutine = null;
        }
    }

    private IEnumerator SlideRoutine(bool show, System.Action onComplete)
    {
        float duration = Mathf.Max(0.01f, slideDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // Clamp the step: the frame that follows a scene load can report a delta of
            // hundreds of milliseconds, which would otherwise finish the slide instantly.
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float t = Mathf.Clamp01(elapsed / duration);

            // Sinusoidal timing (sine in/out): both groups start and finish slowly, so the
            // title visibly drops in and the options visibly rise rather than snapping.
            float eased = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI);
            float progress = show ? eased : 1f - eased;

            ApplySlide(progress);

            yield return null;
        }

        ApplySlide(show ? 1f : 0f);
        slideRoutine = null;

        if (show)
        {
            if (panelCanvasGroup != null)
                panelCanvasGroup.interactable = true;

            if (defaultSelectedButton != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(defaultSelectedButton.gameObject);
        }

        onComplete?.Invoke();
    }

    // 0 puts the title above its home and the options below theirs; 1 is the authored layout.
    private void ApplySlide(float progress)
    {
        if (titleHolder != null)
            titleHolder.anchoredPosition = Vector2.Lerp(titleHome + Vector2.up * slideDistance, titleHome, progress);

        if (optionsHolder != null)
            optionsHolder.anchoredPosition = Vector2.Lerp(optionsHome + Vector2.down * slideDistance, optionsHome, progress);
    }
}

