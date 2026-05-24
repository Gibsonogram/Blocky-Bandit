using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SettingsUI : UIScreen
{
    public static SettingsUI Instance { get; private set; }
    [SerializeField] private Button defaultSelectedButton;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void OnBack()
    {
        UINavigator.Instance.Pop();
    }
}
