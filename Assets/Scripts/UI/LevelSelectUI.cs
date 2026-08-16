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
    [SerializeField] private TMP_Text collectableInfoText;
    [SerializeField] private CanvasGroup panelCanvasGroup;
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
    }

    public void Configure(WorldData world, int worldIndex)
    {
        configuredWorld = world;
        configuredWorldIndex = worldIndex;
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
        PopulateButtons(configuredWorld, configuredWorldIndex);

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
            levelButton.OnHoverEnter += ShowCollectableInfo;
            levelButton.OnHoverExit += ClearCollectableInfo;
            levelButton.OnActivate += HandleButtonActivation;

            if (firstButton == null)
                firstButton = button;
        }

        if (firstButton == null)
            return;

        hoveredLevelIndex = 0;
        ShowCollectableInfo(worldIndex, 0);
        focusCoroutine = StartCoroutine(SelectFirstButtonNextFrame(firstButton));
    }

    private void HandleButtonActivation(int levelIndex)
    {
        hoveredLevelIndex = levelIndex;
        OnSelect();
    }

    private IEnumerator SelectFirstButtonNextFrame(Button firstButton)
    {
        yield return null;
        focusCoroutine = null;

        if (!isActiveAndEnabled || firstButton == null || !firstButton.gameObject.activeInHierarchy || EventSystem.current == null)
            yield break;

        EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
    }

    private void ShowCollectableInfo(int worldIndex, int levelIndex)
    {
        if (collectableInfoText == null || SaveManager.Instance == null)
            return;

        int best = SaveManager.Instance.GetBestCollectables(worldIndex, levelIndex);
        collectableInfoText.text = best > 0 ? $"Best: {best} collectables" : "0";
    }

    private void ClearCollectableInfo()
    {
        if (collectableInfoText != null)
            collectableInfoText.text = string.Empty;
    }

    private void ClearButtons()
    {
        if (buttonContainer == null)
            return;

        for (int i = buttonContainer.childCount - 1; i >= 0; i--)
            Destroy(buttonContainer.GetChild(i).gameObject);
    }
}
