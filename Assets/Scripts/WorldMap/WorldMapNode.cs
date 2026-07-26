using UnityEngine;

public class WorldMapNode : MonoBehaviour
{
    [SerializeField] private WorldData worldData;
    [SerializeField] private GameObject lockedIndicator;
    [SerializeField] private GameObject unlockedIndicator;

    public WorldData WorldData => worldData;

    public void SetLocked(bool locked)
    {
        if (lockedIndicator != null) lockedIndicator.SetActive(locked);
        if (unlockedIndicator != null) unlockedIndicator.SetActive(!locked);
    }
}
