using UnityEngine;

[CreateAssetMenu(menuName = "Game/World Data")]
public class WorldData : ScriptableObject
{
    public string worldName;
    public string[] levelSceneNames;
    public int[] levelCollectableTotals;

    public int GetTotalCollectables()
    {
        int total = 0;
        foreach (int t in levelCollectableTotals) total += t;
        return total;
    }

    public int GetFoundCollectables()
    {
        int found = 0;
        // worldIndex is not known here — caller must provide it via SaveManager queries
        // This overload is not used directly; WorldMapNode calls the indexed version
        return found;
    }

    public int GetFoundCollectables(int worldIndex)
    {
        int found = 0;
        for (int i = 0; i < levelSceneNames.Length; i++)
            found += SaveManager.Instance.GetBestCollectables(worldIndex, i);
        return found;
    }
}
