using UnityEngine;

[CreateAssetMenu(menuName = "Game/World Data")]
public class WorldData : ScriptableObject
{
    public string worldName;
    public string[] levelSceneNames;
    public int[] levelCollectableTotals;
    public int collectableUnlockThreshold;

    public bool TryGetLevelCollectableTotal(int levelIndex, out int totalCollectables)
    {
        totalCollectables = 0;

        if (levelCollectableTotals == null || levelIndex < 0 || levelIndex >= levelCollectableTotals.Length)
            return false;

        totalCollectables = levelCollectableTotals[levelIndex];
        return totalCollectables >= 0;
    }
}
