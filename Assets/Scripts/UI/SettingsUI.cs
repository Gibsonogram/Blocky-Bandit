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
    [SerializeField] private Slider opacitySlider;
    [SerializeField] private TMP_Text opacityLabel;
    [SerializeField] private Button shapeButton;
    [SerializeField] private string sfxLabelFormat = "Sound Effects: {0}";
    [SerializeField] private string musicLabelFormat = "Music: {0}";
    [SerializeField] private string opacityLabelFormat = "Danger Overlay Opacity: {0}%";
    [SerializeField] private string shapeLabelFormat = "Danger Overlay Shape: {0}";

    private TMP_Text shapeLabel;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Bind both overlay controls in code so the scene keeps no hand-wired events. The
        // slider's label is authored, and the shape label is read straight from its button.
        if (opacitySlider != null)
            opacitySlider.onValueChanged.AddListener(OnOpacityChanged);

        if (shapeButton != null)
        {
            shapeLabel = shapeButton.GetComponentInChildren<TMP_Text>(true);
            shapeButton.onClick.AddListener(OnCycleShape);
        }
    }

    public override void Show()
    {
        base.Show();

        if (opacitySlider != null)
            opacitySlider.SetValueWithoutNotify(SaveManager.OverlayOpacity);

        RefreshLabels();

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
        RefreshLabels();
    }

    public void OnToggleMusic()
    {
        if (AudioManager.Instance == null)
            return;

        AudioManager.Instance.ToggleMusic();
        RefreshLabels();
    }

    // Slider drags arrive continuously; round to whole percent so the saved value and the
    // label agree, and only write the preference when the rounded value actually changes.
    public void OnOpacityChanged(float value)
    {
        float rounded = Mathf.Round(Mathf.Clamp01(value) * 100f) * 0.01f;

        if (!Mathf.Approximately(SaveManager.OverlayOpacity, rounded))
            SaveManager.OverlayOpacity = rounded;

        VisionOverlayRenderer.SetGlobalOpacity(rounded);
        RefreshLabels();
    }

    // Cycles Circle -> Diamond -> Square. The saved value applies to every level scene from
    // now on, and the loaded overlay (if any) repaints when play resumes.
    public void OnCycleShape()
    {
        int count = System.Enum.GetValues(typeof(VisionOverlayShape)).Length;
        int next = ((int)SaveManager.OverlayShape + 1) % count;

        SaveManager.OverlayShape = (VisionOverlayShape)next;
        VisionOverlayRenderer.SetGlobalShape((VisionOverlayShape)next);
        RefreshLabels();
    }

    public void OnBack() => UINavigator.Instance.OnBack();

    private void RefreshLabels()
    {
        bool sfxOn = AudioManager.Instance == null || AudioManager.Instance.SfxEnabled;
        bool musicOn = AudioManager.Instance == null || AudioManager.Instance.MusicEnabled;

        if (sfxLabel != null)
            sfxLabel.text = string.Format(sfxLabelFormat, sfxOn ? "On" : "Off");

        if (musicLabel != null)
            musicLabel.text = string.Format(musicLabelFormat, musicOn ? "On" : "Off");

        float opacity = SaveManager.OverlayOpacity;
        if (opacityLabel != null)
            opacityLabel.text = string.Format(opacityLabelFormat, Mathf.RoundToInt(opacity * 100f));

        if (shapeLabel != null)
            shapeLabel.text = string.Format(shapeLabelFormat, SaveManager.OverlayShape);
    }
}
