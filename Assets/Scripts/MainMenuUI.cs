using UnityEngine;

public class MainMenuUI : MonoBehaviour
{
    public void OnPlay() => LevelManager.Instance.LoadWorldMap();
    public void OnQuit() => Application.Quit();
}
