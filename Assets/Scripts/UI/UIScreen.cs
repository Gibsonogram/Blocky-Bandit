using UnityEngine;

// Base class for all UI screens managed by UINavigator.
public abstract class UIScreen : MonoBehaviour
{
    [SerializeField] protected GameObject panel;

    public virtual void Show() => panel.SetActive(true);
    public virtual void Hide() => panel.SetActive(false);
}
