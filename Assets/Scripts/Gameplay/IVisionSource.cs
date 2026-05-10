using System.Collections.Generic;
using UnityEngine;

public interface IVisionSource
{
    IEnumerable<Vector2Int> GetVisibleTiles();
}
