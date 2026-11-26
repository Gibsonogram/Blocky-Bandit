using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class LevelSelectUI : UIScreen
{
    public static LevelSelectUI Instance { get; private set; }

    [SerializeField] private Transform buttonContainer;
    [SerializeField] private GameObject levelButtonPrefab;
    [SerializeField] private TMP_Text worldTitleText;
    [SerializeField] private CanvasGroup panelCanvasGroup;
    [SerializeField] private ScrollRect levelScrollRect;
    [SerializeField] private ScrollRowSnapper listSnapper;
    [SerializeField] private float scrollPadding = 12f;
    [SerializeField] private float fadeDuration = 0.3f;

    private WorldData configuredWorld;
    private int configuredWorldIndex;
    private int hoveredLevelIndex = -1;
    private Coroutine focusCoroutine;
    private Tween fadeTween;
    private bool isInteractable;
    private bool transitionLocked;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // The snapper lives on the scroll view itself; an authored reference wins if one is set.
        if (listSnapper == null && levelScrollRect != null)
            listSnapper = levelScrollRect.GetComponent<ScrollRowSnapper>();

        // The world title is decoration; it must not swallow clicks meant for the level list.
        if (worldTitleText != null)
            worldTitleText.raycastTarget = false;
    }

    // The UI input module deselects on a background click (and a click on a row while the panel
    // is still fading deselects too). With no selected object, arrow keys have nowhere to start,
    // so focus is put back on the row the player was last on.
    private void Update()
    {
        if (!isInteractable || transitionLocked || EventSystem.current == null)
            return;

        if (EventSystem.current.currentSelectedGameObject != null)
            return;

        SelectButtonAt(hoveredLevelIndex);
    }

    public void Configure(WorldData world, int worldIndex)
    {
        configuredWorld = world;
        configuredWorldIndex = worldIndex;
        RefreshWorldTitle();
    }

    // The world's own name sits above the level list, so the screen always says which world it is
    // showing. Names are authored per world in WorldData ("Castle Entrance", "Forest Facade", ...).
    private void RefreshWorldTitle()
    {
        if (worldTitleText == null)
            return;

        worldTitleText.text = configuredWorld != null ? configuredWorld.worldName : string.Empty;
    }

    public override void Show()
    {
        base.Show();
        isInteractable = false;
        transitionLocked = true;
        fadeTween?.Kill();
        panelCanvasGroup.alpha = 0f;
        panelCanvasGroup.interactable = false;
        panelCanvasGroup.blocksRaycasts = true;

        EventSystem.current.SetSelectedGameObject(null);
        RefreshWorldTitle();
        PopulateButtons(configuredWorld, configuredWorldIndex);
        ResetScrollPosition();

        fadeTween = panelCanvasGroup.DOFade(1f, fadeDuration).SetUpdate(true).OnComplete(() =>
        {
            if (panel == null || !panel.activeInHierarchy)
                return;

            isInteractable = true;
            transitionLocked = false;
            panelCanvasGroup.interactable = true;
            panelCanvasGroup.blocksRaycasts = true;
        });
    }

    public override void Hide()
    {
        fadeTween?.Kill();
        fadeTween = null;

        if (focusCoroutine != null)
        {
            StopCoroutine(focusCoroutine);
            focusCoroutine = null;
        }

        isInteractable = false;
        transitionLocked = true;
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
        }

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        ClearButtons();
        base.Hide();
    }

    public void OnSelect()
    {
        if (!isInteractable || transitionLocked)
        return;

        if (configuredWorld == null ||
            hoveredLevelIndex < 0 ||
            hoveredLevelIndex >= configuredWorld.levelSceneNames.Length)
            return;

        transitionLocked = true;
        isInteractable = false;
        LevelManager.Instance.LoadLevel(configuredWorldIndex, hoveredLevelIndex);
    }

    private void PopulateButtons(WorldData world, int worldIndex)
    {
        ClearButtons();
        hoveredLevelIndex = -1;
        Button firstButton = null;

        for (int levelIndex = 0; levelIndex < world.levelSceneNames.Length; levelIndex++)
        {
            GameObject buttonObject = Instantiate(levelButtonPrefab, buttonContainer);
            Button button = buttonObject.GetComponent<Button>();
            if (button == null)
            {
                Debug.LogWarning("LevelSelectUI requires the level button prefab root to contain a Button component.", buttonObject);
                Destroy(buttonObject);
                continue;
            }

            LevelButtonUI levelButton = buttonObject.GetComponent<LevelButtonUI>();

            if (!world.TryGetLevelCollectableTotal(levelIndex, out int totalCollectables))
            {
                Debug.LogWarning($"LevelSelectUI skipped progress total for level {levelIndex} in '{world.name}'.", this);
                totalCollectables = 0;
            }

            levelButton.Initialize(worldIndex, levelIndex, totalCollectables);
            levelButton.OnHoverEnter += HandleLevelHover;
            levelButton.OnFocus += HandleLevelFocus;
            levelButton.OnActivate += HandleButtonActivation;

            if (firstButton == null)
                firstButton = button;
        }

        if (firstButton == null)
            return;

        hoveredLevelIndex = 0;
        focusCoroutine = StartCoroutine(SelectFirstButtonNextFrame(firstButton));
    }

    private void HandleButtonActivation(int levelIndex)
    {
        hoveredLevelIndex = levelIndex;
        OnSelect();
    }

    // Hover and keyboard/gamepad focus both remember the row, so a later collapse of focus can be
    // restored to the row the player was actually looking at.
    private void HandleLevelHover(int worldIndex, int levelIndex)
    {
        hoveredLevelIndex = levelIndex;
    }

    private void HandleLevelFocus(int levelIndex)
    {
        hoveredLevelIndex = levelIndex;
        ScrollLevelIntoView(levelIndex);
    }

    private void SelectButtonAt(int levelIndex)
    {
        if (buttonContainer == null || buttonContainer.childCount == 0)
            return;

        int clampedIndex = Mathf.Clamp(levelIndex, 0, buttonContainer.childCount - 1);
        Transform button = buttonContainer.GetChild(clampedIndex);
        if (button != null && button.gameObject.activeInHierarchy)
            EventSystem.current.SetSelectedGameObject(button.gameObject);
    }

    private IEnumerator SelectFirstButtonNextFrame(Button firstButton)
    {
        yield return null;
        focusCoroutine = null;

        if (!isActiveAndEnabled || firstButton == null || !firstButton.gameObject.activeInHierarchy || EventSystem.current == null)
            yield break;

        EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
    }

    // The level list lives in a ScrollRect because a world can hold more levels than the
    // panel is tall. Always start at the top when the screen opens instead of wherever the
    // previous world (or a previous visit) left the content.
    private void ResetScrollPosition()
    {
        if (listSnapper != null)
        {
            listSnapper.ResetToTop();
            return;
        }

        if (levelScrollRect == null || levelScrollRect.content == null)
            return;

        levelScrollRect.StopMovement();
        levelScrollRect.content.anchoredPosition = Vector2.zero;
    }

    // UGUI moves selection between buttons without touching the ScrollRect, so a level picked
    // with a keyboard or gamepad can end up outside the viewport. The snapper brings the row
    // fully into view and leaves the list resting on a row boundary, so the focused level is
    // never left sliced by the edge of the list.
    private void ScrollLevelIntoView(int levelIndex)
    {
        if (buttonContainer == null || listSnapper == null)
            return;

        if (levelIndex < 0 || levelIndex >= buttonContainer.childCount)
            return;

        if (buttonContainer.GetChild(levelIndex) is RectTransform target)
            listSnapper.EnsureVisible(target, scrollPadding);
    }

    private void ClearButtons()
    {
        if (buttonContainer == null)
            return;

        for (int i = buttonContainer.childCount - 1; i >= 0; i--)
            Destroy(buttonContainer.GetChild(i).gameObject);
    }
}
