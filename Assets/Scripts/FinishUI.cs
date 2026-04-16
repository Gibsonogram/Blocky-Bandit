using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class FinishUI : MonoBehaviour
{

    [SerializeField] private GameObject panel;
    [SerializeField] private Button defaultSelectedButton;
    [SerializeField] private GameObject[] collectableSlotsFilled;

    void Start()
    {
        panel.SetActive(false);
        ResetCollectableSlots();
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
            int found = CollectableManager.Instance.foundCollectables;
            EventSystem.current.SetSelectedGameObject(defaultSelectedButton.gameObject);
            FillCollectableSlots(found);
        }
    }


    void ResetCollectableSlots()
    {
        foreach (GameObject slot in collectableSlotsFilled)
            slot.SetActive(false);
    }

    void FillCollectableSlots(int found)
    {
        ResetCollectableSlots();
        for (int i = 0; i < found && i < collectableSlotsFilled.Length; i++)
            collectableSlotsFilled[i].SetActive(true);
    }


    public void OnReplay() => LevelManager.Instance.ReplayLevel();
    public void OnNextLevel() => LevelManager.Instance.LoadNextLevel();
    public void OnMainMenu() => LevelManager.Instance.LoadMainMenu();



}
