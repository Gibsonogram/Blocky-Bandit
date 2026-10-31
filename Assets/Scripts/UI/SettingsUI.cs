using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SettingsUI : UIScreen
{
    public static SettingsUI Instance { get; private set; }

    [SerializeField] private Button defaultSelectedButton;
    [SerializeField] private ControlsUI controlsUI;
    [SerializeField] private TMP_Text sfxLabel;
    [SerializeField] private TMP_Text musicLabel;
    [SerializeField] private string sfxLabelFormat = "Sound Effects: {0}";
    [SerializeField] private string musicLabelFormat = "Music: {0}";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void Show()
    {
        base.Show();
        RefreshAudioLabels();

        if (defaultSelectedButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(defaultSelectedButton.gameObject);
    }

    public void OnControls()
    {
        if (controlsUI == null)
        {
            Debug.LogWarning("SettingsUI has no controls screen assigned.", this);
            return;
        }

        UINavigator.Instance.Push(controlsUI);
    }

    public void OnToggleSfx()
    {
        if (AudioManager.Instance == null)
            return;

        AudioManager.Instance.ToggleSfx();
        RefreshAudioLabels();
    }

    public void OnToggleMusic()
    {
        if (AudioManager.Instance == null)
            return;

        AudioManager.Instance.ToggleMusic();
        RefreshAudioLabels();
    }

    public void OnBack() => UINavigator.Instance.OnBack();

    private void RefreshAudioLabels()
    {
        bool sfxOn = AudioManager.Instance == null || AudioManager.Instance.SfxEnabled;
        bool musicOn = AudioManager.Instance == null || AudioManager.Instance.MusicEnabled;

        if (sfxLabel != null)
            sfxLabel.text = string.Format(sfxLabelFormat, sfxOn ? "On" : "Off");

        if (musicLabel != null)
            musicLabel.text = string.Format(musicLabelFormat, musicOn ? "On" : "Off");
    }
}
