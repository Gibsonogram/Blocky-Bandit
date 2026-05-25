using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class WorldSelectUI : UIScreen
{
    public static WorldSelectUI Instance { get; private set; }

    [SerializeField] private Transform buttonContainer;
    [SerializeField] private Button worldButtonPrefab;

    private int hoveredWorldIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void Show()
    {
        base.Show();
        PopulateButtons();
    }

    public override void Hide()
    {
        base.Hide();
        ClearButtons();
    }

    public void OnSelect()
    {
        LevelManager.Instance.SelectWorld(hoveredWorldIndex);
    }

    void PopulateButtons()
    {
        ClearButtons();
        WorldData[] worlds = LevelManager.Instance.Worlds;
        Button firstButton = null;

        for (int i = 0; i < worlds.Length; i++)
        {
            int worldIndex = i;
            Button btn = Instantiate(worldButtonPrefab, buttonContainer);
            btn.GetComponentInChildren<TMP_Text>().text = worlds[i].worldName;

            // Track which world is highlighted for OnSelect
            EventTrigger trigger = btn.gameObject.AddComponent<EventTrigger>();
            EventTrigger.Entry entry = new() { eventID = EventTriggerType.Select };
            entry.callback.AddListener(_ => hoveredWorldIndex = worldIndex);
            trigger.triggers.Add(entry);

            btn.onClick.AddListener(() => LevelManager.Instance.SelectWorld(worldIndex));

            if (firstButton == null)
                firstButton = btn;
        }

        if (firstButton != null)
        {
            hoveredWorldIndex = 0;
            EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
        }
    }

    void ClearButtons()
    {
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);
    }
}
