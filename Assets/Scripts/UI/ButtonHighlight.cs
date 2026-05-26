using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonHighlight : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    [SerializeField] private GameObject highlightGraphic;

    public void OnSelect(BaseEventData eventData) => highlightGraphic.SetActive(true);
    public void OnDeselect(BaseEventData eventData) => highlightGraphic.SetActive(false);
    private void OnEnable() { if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject) highlightGraphic.SetActive(true); }
    private void OnDisable() => highlightGraphic.SetActive(false);
}
