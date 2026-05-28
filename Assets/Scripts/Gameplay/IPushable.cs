using UnityEngine;

public interface IPushable
{
    Vector2Int GridPosition { get; }
    void ExecutePush(Vector2Int direction);
    void ExecuteBump(Vector2Int direction);
}
