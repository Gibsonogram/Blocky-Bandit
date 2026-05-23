using UnityEngine;

public class MainMenuUI : MonoBehaviour
{
    public void OnPlay() => LevelManager.Instance.LoadWorldSelect();
    public void OnQuit() => Application.Quit();
}
