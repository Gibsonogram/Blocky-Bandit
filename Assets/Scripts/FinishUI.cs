using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class FinishUI : MonoBehaviour
{

    [SerializeField] private GameObject panel;
    [SerializeField] private Button defaultSelectedButton;

    void Start()
    {
        panel.SetActive(false); 
        GameStateManager.Instance.OnStateChanged += OnStateChanged;
    }

    void OnDestroy()
    {
        GameStateManager.Instance.OnStateChanged -= OnStateChanged;
    }

    void OnStateChanged(GameState newState)
    {
        bool inEndScreen = newState == GameState.EndScreen;
        panel.SetActive(inEndScreen);

        if (inEndScreen)
        {
            EventSystem.current.SetSelectedGameObject(defaultSelectedButton.gameObject);
        }
    }

    public void OnReplay() => GameStateManager.Instance.ChangeState(GameState.PlayMode);
    public void OnNextLevel() => GameStateManager.Instance.ChangeState(GameState.NextLevel);
    public void OnMainMenu() => GameStateManager.Instance.ChangeState(GameState.MainMenu);



}
