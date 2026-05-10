using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class WorldMapNode : MonoBehaviour, ISelectHandler, ISubmitHandler, ICancelHandler
{
    [SerializeField] private int worldIndex;
    [SerializeField] private WorldData worldData;

    public int WorldIndex => worldIndex;
    public WorldData WorldData => worldData;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    private void Start()
    {
        WorldMapManager.Instance.RegisterNode(this);
    }

    public void SetUnlocked(bool unlocked)
    {
        button.interactable = unlocked;
    }

    public bool IsUnlocked()
    {
        if (worldIndex == 0) return true;
        for (int i = 0; i < worldData.levelSceneNames.Length; i++)
        {
            if (SaveManager.Instance.GetBestCollectables(worldIndex - 1, i) > 0)
                return true;
        }
        return false;
    }

    public void OnSelect(BaseEventData eventData)
    {
        WorldMapCursor.Instance.MoveToNode(this);
        WorldMapManager.Instance.ShowPreview(this);
    }

    public void OnSubmit(BaseEventData eventData)
    {
        LevelManager.Instance.SelectWorld(worldIndex);
    }

    public void OnCancel(BaseEventData eventData)
    {
        LevelManager.Instance.LoadMainMenu();
    }
}
