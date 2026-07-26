using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class LevelButtonUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, ISelectHandler, ISubmitHandler
{
    public event Action<int, int> OnHoverEnter;
    public event Action OnHoverExit;
    public event Action<int> OnActivate;

    [SerializeField] private TMP_Text levelLabel;
    [SerializeField] private TMP_Text progressLabel;

    private int worldIndex;
    private int levelIndex;
    private bool initialized;

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
        if (initialized)
            OnHoverEnter?.Invoke(worldIndex, levelIndex);
    }

    public void OnPointerExit(PointerEventData eventData) => OnHoverExit?.Invoke();

    public void OnPointerClick(PointerEventData eventData)
    {
        if (initialized)
            OnActivate?.Invoke(levelIndex);
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (initialized)
            OnHoverEnter?.Invoke(worldIndex, levelIndex);
    }

    public void OnSubmit(BaseEventData eventData)
    {
        if (initialized)
            OnActivate?.Invoke(levelIndex);
    }
}
