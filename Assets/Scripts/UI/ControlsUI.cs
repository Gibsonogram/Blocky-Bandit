using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Read-only cheat sheet pushed from the options menu. Its content is authored in the
// scene; the script only handles focus and the back button.
public class ControlsUI : UIScreen
{
    public static ControlsUI Instance { get; private set; }

    [SerializeField] private Button defaultSelectedButton;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void Show()
    {
        base.Show();

        if (defaultSelectedButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(defaultSelectedButton.gameObject);
    }

    public void OnBack() => UINavigator.Instance.OnBack();
}
