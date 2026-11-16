using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The danger overlay's player-facing options, pushed on top of the Options screen so Back
// returns there. Everything on this page is a saved preference; the authored look (colours,
// fill alpha, outline) stays in VisionOverlayStyle and is deliberately not exposed.
public class VisualSettingsUI : UIScreen
{
    public static VisualSettingsUI Instance { get; private set; }

    [SerializeField] private Button defaultSelectedButton;
    [SerializeField] private Slider opacitySlider;
    [SerializeField] private TMP_Text opacityLabel;
    [SerializeField] private Slider radiusSlider;
    [SerializeField] private TMP_Text radiusLabel;
    [SerializeField] private Button colorblindButton;
    [SerializeField] private TMP_Text colorblindLabel;
    [SerializeField] private Button resetButton;
    [SerializeField] private Button backButton;
    [SerializeField] private string opacityLabelFormat = "Danger Overlay Opacity: {0}%";
    [SerializeField] private string radiusLabelFormat = "Danger Overlay Size: {0}";
    [SerializeField] private string colorblindLabelFormat = "Colorblind Mode: {0}";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // The slider's range is owned by VisionOverlayDefaults, so the scene cannot drift from
        // the clamp SaveManager applies. Bound in code: the scene keeps no hand-wired events.
        if (radiusSlider != null)
        {
            radiusSlider.minValue = VisionOverlayDefaults.MinRadius;
            radiusSlider.maxValue = VisionOverlayDefaults.MaxRadius;
            radiusSlider.wholeNumbers = false;
            radiusSlider.onValueChanged.AddListener(OnRadiusChanged);
        }

        if (opacitySlider != null)
            opacitySlider.onValueChanged.AddListener(OnOpacityChanged);

        if (colorblindButton != null)
            colorblindButton.onClick.AddListener(OnColorblindMode);

        if (resetButton != null)
            resetButton.onClick.AddListener(OnResetToDefaults);

        if (backButton != null)
            backButton.onClick.AddListener(OnBack);
    }

    public override void Show()
    {
        base.Show();

        SyncControls();

        if (defaultSelectedButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(defaultSelectedButton.gameObject);
    }

    // Slider drags arrive continuously; round to whole percent so the saved value and the label
    // agree, and only write the preference when the rounded value actually changes.
    public void OnOpacityChanged(float value)
    {
        float rounded = Mathf.Round(Mathf.Clamp01(value) * 100f) * 0.01f;

        if (!Mathf.Approximately(SaveManager.OverlayOpacity, rounded))
            SaveManager.OverlayOpacity = rounded;

        VisionOverlayRenderer.SetGlobalOpacity(rounded);
        RefreshLabels();
    }

    // Rounded to hundredths for a stable label. Masks are rasterised at whole art pixels, so
    // values inside the same pixel step produce the same size.
    public void OnRadiusChanged(float value)
    {
        float clamped = Mathf.Clamp(value, VisionOverlayDefaults.MinRadius, VisionOverlayDefaults.MaxRadius);
        float rounded = Mathf.Round(clamped * 100f) * 0.01f;

        if (!Mathf.Approximately(SaveManager.OverlayRadius, rounded))
            SaveManager.OverlayRadius = rounded;

        VisionOverlayRenderer.SetGlobalRadius(rounded);
        RefreshLabels();
    }

    // Placeholder: there is no colour-blind palette yet, so the row exists but does nothing
    // beyond telling the player it is coming.
    public void OnColorblindMode() =>
        Debug.Log("Visual settings: colourblind mode is not implemented yet.", this);

    public void OnResetToDefaults()
    {
        SaveManager.ResetOverlayDefaults();
        VisionOverlayRenderer.ApplySavedSettings();
        SyncControls();
    }

    public void OnBack() => UINavigator.Instance.OnBack();

    // Pulls the saved preferences into the controls without firing their change callbacks.
    private void SyncControls()
    {
        if (opacitySlider != null)
            opacitySlider.SetValueWithoutNotify(SaveManager.OverlayOpacity);

        if (radiusSlider != null)
            radiusSlider.SetValueWithoutNotify(SaveManager.OverlayRadius);

        RefreshLabels();
    }

    private void RefreshLabels()
    {
        if (opacityLabel != null)
            opacityLabel.text = string.Format(opacityLabelFormat, Mathf.RoundToInt(SaveManager.OverlayOpacity * 100f));

        if (radiusLabel != null)
            radiusLabel.text = string.Format(radiusLabelFormat, SaveManager.OverlayRadius.ToString("0.00"));

        if (colorblindLabel != null)
            colorblindLabel.text = string.Format(colorblindLabelFormat, "Off");
    }
}
