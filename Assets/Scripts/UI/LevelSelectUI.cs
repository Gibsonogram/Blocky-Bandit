using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

// Attach to the level select panel in WorldMap.unity.
// Populate levelButtonPrefab with a Button prefab — its label will be set to the level number.
public class LevelSelectUI : UIScreen
{
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private Button levelButtonPrefab;
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text collectableInfoText;

    private WorldData configuredWorld;
    private int configuredWorldIndex;

    private void Start()
    {
        backButton.onClick.AddListener(OnBack);
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

    void PopulateButtons(WorldData world, int worldIndex)
    {
        ClearButtons();
        Button firstButton = null;
        for (int i = 0; i < world.levelSceneNames.Length; i++)
        {
            int levelIndex = i;
            Button btn = Instantiate(levelButtonPrefab, buttonContainer);
            btn.GetComponentInChildren<TMP_Text>().text = $"{i + 1}";
            btn.onClick.AddListener(() => LevelManager.Instance.LoadLevel(worldIndex, levelIndex));

            LevelButtonUI hoverHandler = btn.gameObject.AddComponent<LevelButtonUI>();
            hoverHandler.Initialize(worldIndex, levelIndex);
            hoverHandler.OnHoverEnter += ShowCollectableInfo;
            hoverHandler.OnHoverExit += ClearCollectableInfo;

            if (firstButton == null)
            {
                firstButton = btn;
            }
        }
        if (firstButton)
        {
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

    void OnBack()
    {
        UINavigator.Instance.Pop();
    }
}
