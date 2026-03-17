using UnityEngine;

public class FinishUI : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        GameStateManager.Instance.OnStateChanged += OnStateChanged;
    }

    void OnDestroy()
    {
        GameStateManager.Instance.OnStateChanged -= OnStateChanged;
    }

    void OnStateChanged(GameState newState)
    {
        bool inEndScreen = newState == GameState.EndScreen;
        gameObject.SetActive(inEndScreen);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
