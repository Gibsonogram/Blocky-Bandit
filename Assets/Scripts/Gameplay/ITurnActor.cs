using UnityEngine;

public interface ITurnActor 
{
    void TakeTurn();
    void OnDefeat();

    Vector2Int GridPosition { get; }
    int CombatPriority { get; }
}
