using UnityEngine;
using System;

public static class CombatEvents
{
    public static event Action<Vector2Int, GameObject> Defeat;

    public static void RaiseDefeat(Vector2Int gridPos, GameObject corpsePrefab) => Defeat?.Invoke(gridPos, corpsePrefab);    
}
