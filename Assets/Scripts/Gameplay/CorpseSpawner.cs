using Unity.Mathematics;
using UnityEngine;
using static GridUtils;

public class CorpseSpawner : MonoBehaviour
{
    private void OnEnable() => CombatEvents.Defeat += SpawnCorpse;
    private void OnDisable() => CombatEvents.Defeat -= SpawnCorpse;

    private void SpawnCorpse(Vector2Int gridPos, GameObject corpsePrefab)
    {
        if (corpsePrefab == null) return;
        Instantiate(corpsePrefab, GridToWorld(gridPos), Quaternion.identity);
    }
}
