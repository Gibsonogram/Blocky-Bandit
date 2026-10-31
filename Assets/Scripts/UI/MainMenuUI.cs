using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Lives in the MainMenu scene (not the persistent managers root) so the menu's
// background art and any future menu animation can be authored in that scene. It
// therefore pushes itself onto the navigator once the scene has loaded.
public class MainMenuUI : UIScreen
{
    public static MainMenuUI Instance { get; private set; }

    [SerializeField] private Button defaultSelectedButton;

    public override bool CanGoBack => false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (UINavigator.Instance == null || GameStateManager.Instance == null)
            return;

        if (GameStateManager.Instance.CurrentState != GameState.Menus)
            GameStateManager.Instance.ChangeState(GameState.Menus);

        UINavigator.Instance.Push(this);
    }

    public override void Show()
    {
        base.Show();

        if (defaultSelectedButton == null || EventSystem.current == null)
            return;

        EventSystem.current.SetSelectedGameObject(defaultSelectedButton.gameObject);
    }

    public void OnWorldSelect()
    {
        LevelManager.Instance.LoadWorldMap();
    }

    public void OnSettings()
    {
        if (SettingsUI.Instance == null)
        {
            Debug.LogWarning("MainMenuUI cannot open settings because SettingsUI is missing.", this);
            return;
        }

        UINavigator.Instance.Push(SettingsUI.Instance);
    }

    public void OnQuit() => Application.Quit();
}
