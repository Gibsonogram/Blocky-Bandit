using UnityEngine;

// Disables renderer overlays outside of gameplay states. Attach to Persistent managers.
public class OverlayController : MonoBehaviour
{
    private void Start()
    {
        GameStateManager.Instance.OnStateChanged += OnStateChanged;
        OnStateChanged(GameStateManager.Instance.CurrentState);
    }

    private void OnDestroy()
    {
        GameStateManager.Instance.OnStateChanged -= OnStateChanged;
    }

    private void OnStateChanged(GameState newState)
    {
        bool isGameplay = newState == GameState.PlayMode || newState == GameState.EndScreen;
        GridOverlayRendererFeature.Instance?.SetOpacity(isGameplay ? 1f : 0f);
    }
}
