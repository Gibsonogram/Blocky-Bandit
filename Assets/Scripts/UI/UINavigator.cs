using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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

    public void OnCancel(InputValue val)
    {
        if (!val.isPressed) return;
        Pop();
        if (screenStack.Count == 0)
        {
            GameStateManager.Instance.ChangeState(GameState.PlayMode);
        }
    }
}
