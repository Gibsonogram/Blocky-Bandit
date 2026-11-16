using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A settings row whose value lives in a slider. The row's own button is the only selectable on
// the row, so the selection - and its highlight - always sits on the row: left/right nudge the
// value, up/down move to the next row.
//
// A Slider is a Selectable as well, and in the automatic navigation graph it is the nearest
// target to its own row. Left in, pressing down from the row dives into the slider, the row
// loses its highlight and the menu stops being navigable - so the slider is taken out of the
// graph and driven from here instead. Pointing at it with the mouse still works.
[RequireComponent(typeof(Selectable))]
public class SliderRow : MonoBehaviour, IMoveHandler
{
    [SerializeField] private Slider slider;
    [Tooltip("Value change per left/right press.")]
    [SerializeField] private float step = 0.05f;

    private void Awake()
    {
        if (slider == null) return;

        Navigation navigation = slider.navigation;
        navigation.mode = Navigation.Mode.None;
        slider.navigation = navigation;
    }

    public void OnMove(AxisEventData eventData)
    {
        if (slider == null) return;

        if (eventData.moveDir == MoveDirection.Left)
            Nudge(-step);
        else if (eventData.moveDir == MoveDirection.Right)
            Nudge(step);
    }

    private void Nudge(float delta)
    {
        slider.value = Mathf.Clamp(slider.value + delta, slider.minValue, slider.maxValue);
    }
}
