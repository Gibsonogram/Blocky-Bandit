using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonHighlight : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    [SerializeField] private GameObject highlightGraphic;

    public void OnSelect(BaseEventData eventData) => highlightGraphic.SetActive(true);
    public void OnDeselect(BaseEventData eventData) => highlightGraphic.SetActive(false);
}
