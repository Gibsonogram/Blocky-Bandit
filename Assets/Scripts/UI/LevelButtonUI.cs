using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class LevelButtonUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
{
    public event Action<int, int> OnHoverEnter;
    public event Action OnHoverExit;
    public event Action<int> OnActivate;

    // Raised only when this button becomes the selected UI element (keyboard/gamepad
    // navigation), unlike OnHoverEnter which a mouse pointer also fires. The level list
    // scrolls on focus so navigating to an off-screen button brings it into view, but
    // hovering with a mouse never fights the scroll position.
    public event Action<int> OnFocus;

    // Screen-space distance the pointer has to travel before a hover counts as real.
    private const float PointerMoveThreshold = 2f;

    [SerializeField] private TMP_Text levelLabel;
    [SerializeField] private TMP_Text progressLabel;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color highlightedColor = new Color(1f, 0.8941177f, 0.7098039f, 1f);

    private Button button;
    private Graphic background;
    private Vector2 spawnPointerPosition;
    private int worldIndex;
    private int levelIndex;
    private bool initialized;
    private bool pointerInside;
    private bool pointerHasMoved;
    private bool hoverActive;
    private bool hasSelection;

    public int LevelIndex => levelIndex;

    private void Awake()
    {
        button = GetComponent<Button>();
        if (button != null)
        {
            background = button.targetGraphic;
            // The tint is applied here instead of by Selectable so that a hover Unity resolves
            // from a parked/stale cursor cannot colour a row nobody is pointing at. The level
            // panel's CanvasGroup starts non-interactable, which leaves Selectable's own render
            // stuck on the Disabled tint, so that has to be cleared explicitly.
            button.transition = Selectable.Transition.None;
            if (background != null)
                background.CrossFadeColor(normalColor, 0f, true, true);
        }

        // Without a mouse device there is no stale position to guard against, so hover is live.
        spawnPointerPosition = ReadPointerPosition();
        pointerHasMoved = Mouse.current == null;
    }

    private void OnEnable()
    {
        pointerInside = false;
        hoverActive = false;
        hasSelection = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        RefreshHighlight();
    }

    private void OnDisable()
    {
        pointerInside = false;
        hoverActive = false;
        hasSelection = false;
        RefreshHighlight();
    }

    private void Update()
    {
        RefreshPointerMovement();
        RefreshHoverState();
    }

    public void Initialize(int worldIndex, int levelIndex, int totalCollectables)
    {
        this.worldIndex = worldIndex;
        this.levelIndex = levelIndex;
        initialized = true;

        if (levelLabel != null)
            levelLabel.text = $"{levelIndex + 1}";

        UpdateProgress(totalCollectables);
    }

    public void UpdateProgress(int totalCollectables)
    {
        if (progressLabel == null)
        {
            Debug.LogWarning($"LevelButtonUI on '{name}' has no progress label reference.", this);
            return;
        }

        int clampedTotalCollectables = Mathf.Max(0, totalCollectables);
        if (SaveManager.Instance == null)
        {
            Debug.LogWarning("LevelButtonUI cannot read progress because SaveManager.Instance is missing.", this);
            progressLabel.text = $"0/{clampedTotalCollectables}";
            return;
        }

        int obtainedCollectables = SaveManager.Instance.GetBestCollectables(worldIndex, levelIndex);
        obtainedCollectables = Mathf.Clamp(obtainedCollectables, 0, clampedTotalCollectables);
        progressLabel.text = $"{obtainedCollectables}/{clampedTotalCollectables}";
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerInside = true;
        RefreshHoverState();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInside = false;
        RefreshHoverState();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (initialized)
            OnActivate?.Invoke(levelIndex);
    }

    public void OnSelect(BaseEventData eventData)
    {
        hasSelection = true;
        RefreshHighlight();

        if (!initialized)
            return;

        OnHoverEnter?.Invoke(worldIndex, levelIndex);
        OnFocus?.Invoke(levelIndex);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        hasSelection = false;
        RefreshHighlight();
    }

    public void OnSubmit(BaseEventData eventData)
    {
        if (initialized)
            OnActivate?.Invoke(levelIndex);
    }

    // A freshly built (or re-shown) row can be handed a PointerEnter by the input module while
    // the cursor is simply parked over that spot - opening the level list from the world map
    // does exactly that. Only treat a hover as real once the pointer has actually moved.
    private void RefreshPointerMovement()
    {
        if (pointerHasMoved)
            return;

        if ((ReadPointerPosition() - spawnPointerPosition).sqrMagnitude >= PointerMoveThreshold * PointerMoveThreshold)
            pointerHasMoved = true;
    }

    private void RefreshHoverState()
    {
        bool hovering = pointerInside && pointerHasMoved;
        if (hovering == hoverActive)
            return;

        hoverActive = hovering;
        RefreshHighlight();

        if (hoverActive)
        {
            if (initialized)
                OnHoverEnter?.Invoke(worldIndex, levelIndex);
        }
        else
        {
            OnHoverExit?.Invoke();
        }
    }

    private void RefreshHighlight()
    {
        if (background == null)
            return;

        background.color = (hoverActive || hasSelection) ? highlightedColor : normalColor;
    }

    private static Vector2 ReadPointerPosition()
    {
        Mouse mouse = Mouse.current;
        return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
    }
}
