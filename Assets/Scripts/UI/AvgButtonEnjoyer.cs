using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Outline))]
public class AvgButtonEnjoyer : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    private Outline outline;
    [SerializeField] private Color selectedColor = Color.white;
    [SerializeField] private Vector2 outlineDistance = new Vector2(4, -4);

    private void Awake()
    {
        outline = GetComponent<Outline>();
        outline.effectColor = selectedColor;
        outline.effectDistance = outlineDistance;
        outline.enabled = false;
    }

    public void OnSelect(BaseEventData eventData) => outline.enabled = true;
    public void OnDeselect(BaseEventData eventData) => outline.enabled = false;

    private void OnEnable() 
    { 
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
            outline.enabled = true; 
    }

    private void OnDisable() => outline.enabled = false;
}
