using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class WorldSelectUI : UIScreen
{
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private Button worldButtonPrefab;
    [SerializeField] private Button backButton;

    private void Start()
    {
        backButton.onClick.AddListener(OnBack);
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
            btn.onClick.AddListener(() => LevelManager.Instance.SelectWorld(worldIndex));

            if (firstButton == null)
                firstButton = btn;
        }

        if (firstButton != null)
            EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
    }

    void ClearButtons()
    {
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);
    }

    void OnBack()
    {
        LevelManager.Instance.LoadMainMenu();
    }
}
