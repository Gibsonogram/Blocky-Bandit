using UnityEngine;

public class CollectableManager : MonoBehaviour
{
    public static CollectableManager Instance { get; private set; }

    [SerializeField] public int totalCollectables = 0;
    [SerializeField] public int foundCollectables = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void RegisterTotal() => totalCollectables += 1;

    public void RegisterCollection() => foundCollectables += 1;

    // Called by LevelManager before updating to a new level, using the outgoing level's indices
    public void SaveBestScore(int worldIndex, int levelIndex)
    {
        if (totalCollectables == 0) return; // level was never played, nothing to save
        SaveManager.Instance.SaveCollectables(worldIndex, levelIndex, foundCollectables);
    }

    public void ResetCount()
    {
        totalCollectables = 0;
        foundCollectables = 0;
    }
}