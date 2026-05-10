using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

// Attach to the level select panel in WorldMap.unity.
public class WorldLevelSelectUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private Button levelButtonPrefab;
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text collectableInfoText;

    private void Start()
    {
        backButton.onClick.AddListener(OnBack);
    }

    public void Show(WorldData world, int worldIndex)
    {
        panel.SetActive(true);
        PopulateButtons(world, worldIndex);
    }

    public void Hide()
    {
        panel.SetActive(false);
        ClearButtons();
    }

    private void PopulateButtons(WorldData world, int worldIndex)
    {
        ClearButtons();
        Button firstButton = null;
        for (int i = 0; i < world.levelSceneNames.Length; i++)
        {
            int levelIndex = i;
            Button btn = Instantiate(levelButtonPrefab, buttonContainer);
            btn.GetComponentInChildren<TMP_Text>().text = $"{i + 1}";
            btn.onClick.AddListener(() => LevelManager.Instance.LoadLevel(worldIndex, levelIndex));

            Navigation nav = btn.navigation;
            nav.mode = Navigation.Mode.Vertical;
            btn.navigation = nav;

            LevelButtonUI hoverHandler = btn.gameObject.AddComponent<LevelButtonUI>();
            hoverHandler.Initialize(worldIndex, levelIndex);
            hoverHandler.OnHoverEnter += ShowCollectableInfo;
            hoverHandler.OnHoverExit += ClearCollectableInfo;

            if (i == 0) firstButton = btn;
        }

        if (firstButton != null)
            EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
    }

    private void ShowCollectableInfo(int worldIndex, int levelIndex)
    {
        int best = SaveManager.Instance.GetBestCollectables(worldIndex, levelIndex);
        int total = LevelManager.Instance.CurrentWorld.levelCollectableTotals.Length > levelIndex
            ? LevelManager.Instance.CurrentWorld.levelCollectableTotals[levelIndex]
            : 0;
        collectableInfoText.text = best > 0 ? $"{best}/{total} collectables" : $"0/{total} — Not yet played";
    }

    private void ClearCollectableInfo() => collectableInfoText.text = string.Empty;

    private void ClearButtons()
    {
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);
    }

    private void OnBack()
    {
        GameStateManager.Instance.ChangeState(GameState.WorldMap);
    }
}
