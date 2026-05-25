using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class LevelSelectUI : UIScreen
{
    public static LevelSelectUI Instance { get; private set; }

    [SerializeField] private Transform buttonContainer;
    [SerializeField] private Button levelButtonPrefab;
    [SerializeField] private TMP_Text collectableInfoText;

    private WorldData configuredWorld;
    private int configuredWorldIndex;
    private int hoveredLevelIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
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
        PopulateButtons(configuredWorld, configuredWorldIndex);
    }

    public override void Hide()
    {
        base.Hide();
        ClearButtons();
    }

    public void OnSelect()
    {
        LevelManager.Instance.LoadLevel(configuredWorldIndex, hoveredLevelIndex);
    }

    void PopulateButtons(WorldData world, int worldIndex)
    {
        ClearButtons();
        Button firstButton = null;

        for (int i = 0; i < world.levelSceneNames.Length; i++)
        {
            int levelIndex = i;
            Button btn = Instantiate(levelButtonPrefab, buttonContainer);
            btn.GetComponentInChildren<TMP_Text>().text = $"{i + 1}";

            // Track which level is highlighted for OnSelect
            EventTrigger trigger = btn.gameObject.AddComponent<EventTrigger>();
            EventTrigger.Entry entry = new() { eventID = EventTriggerType.Select };
            entry.callback.AddListener(_ => { hoveredLevelIndex = levelIndex; ShowCollectableInfo(worldIndex, levelIndex); });
            trigger.triggers.Add(entry);

            btn.onClick.AddListener(() => LevelManager.Instance.LoadLevel(worldIndex, levelIndex));

            LevelButtonUI hoverHandler = btn.gameObject.AddComponent<LevelButtonUI>();
            hoverHandler.Initialize(worldIndex, levelIndex);
            hoverHandler.OnHoverEnter += ShowCollectableInfo;
            hoverHandler.OnHoverExit += ClearCollectableInfo;

            if (firstButton == null)
                firstButton = btn;
        }

        if (firstButton)
        {
            hoveredLevelIndex = 0;
            EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
        }
    }

    void ShowCollectableInfo(int worldIndex, int levelIndex)
    {
        int best = SaveManager.Instance.GetBestCollectables(worldIndex, levelIndex);
        collectableInfoText.text = best > 0 ? $"Best: {best} collectables" : "Not yet played";
    }

    void ClearCollectableInfo() => collectableInfoText.text = string.Empty;

    void ClearButtons()
    {
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);
    }
}
