using UnityEngine;
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
}
