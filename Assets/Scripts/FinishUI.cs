using UnityEngine;

public class FinishUI : MonoBehaviour
{

    [SerializeField] private GameObject panel;

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
    }

}
