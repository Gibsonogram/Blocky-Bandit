using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MainMenuUI : UIScreen
{
    public static MainMenuUI Instance { get; private set; }

    [SerializeField] private Button defaultSelectedButton;
    [SerializeField] private SettingsUI settingsUI;

    public override bool CanGoBack => false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void Show()
    {
        base.Show();
        EventSystem.current.SetSelectedGameObject(defaultSelectedButton.gameObject);
    }

    public void OnWorldSelect()
    {
        LevelManager.Instance.LoadWorldMap();
    }

    public void OnSettings()
    {
        UINavigator.Instance.Push(settingsUI);
    }

    public void OnQuit() => Application.Quit();
}
