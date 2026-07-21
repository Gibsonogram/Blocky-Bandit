using System.Collections.Generic;
using UnityEngine;

// Manages a stack of UIScreens. Push to open a screen, Pop to return to the previous one.
public class UINavigator : MonoBehaviour
{
    public static UINavigator Instance { get; private set; }
    public bool IsEmpty => screenStack.Count == 0;

    private readonly Stack<UIScreen> screenStack = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Push(UIScreen screen)
    {
        if (screenStack.TryPeek(out UIScreen current))
            current.Hide();

        screenStack.Push(screen);
        screen.Show();
    }

    public void Pop()
    {
        if (screenStack.TryPop(out UIScreen current))
            current.Hide();

        if (screenStack.TryPeek(out UIScreen previous))
            previous.Show();
    }

    public void ClearAll()
    {
        while (screenStack.TryPop(out UIScreen screen))
            screen.Hide();
    }

    // Single back entry point for both input and UI buttons.
    public void OnBack()
    {
        GameState state = GameStateManager.Instance.CurrentState;
        if (state != GameState.PauseScreen && state != GameState.Menus) return;
        if (screenStack.TryPeek(out UIScreen current) && !current.CanGoBack) return;

        Pop();

        if (IsEmpty && state == GameState.PauseScreen)
            GameStateManager.Instance.ChangeState(GameState.PlayMode);

        if (IsEmpty && state == GameState.Menus)
            GameStateManager.Instance.ChangeState(GameState.WorldMap);
    }
}
