using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Added at runtime to each level button by LevelSelectUI
public class LevelButtonUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public event Action<int, int> OnHoverEnter;
    public event Action OnHoverExit;

    private int worldIndex;
    private int levelIndex;

    public void Initialize(int worldIndex, int levelIndex)
    {
        this.worldIndex = worldIndex;
        this.levelIndex = levelIndex;
    }

    public void OnPointerEnter(PointerEventData eventData) => OnHoverEnter?.Invoke(worldIndex, levelIndex);
    public void OnPointerExit(PointerEventData eventData) => OnHoverExit?.Invoke();
}
