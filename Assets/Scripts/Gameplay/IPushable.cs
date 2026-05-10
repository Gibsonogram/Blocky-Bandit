using UnityEngine;

public interface IPushable
{
    bool TryGetPushed(Vector2Int direction);
}
