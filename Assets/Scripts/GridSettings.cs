using UnityEngine;
using UnityEngine.Tilemaps;

public class GridSettings : MonoBehaviour
{
    public static GridSettings Instance { get; private set; }

    [SerializeField] private Tilemap collisionTilemap;
    [SerializeField] private Tilemap walkBehindTilemap;
    [SerializeField] private LayerMask actorLayer;
    [SerializeField] private float tileSize = 1f;

    public static Tilemap CollisionTilemap => Instance.collisionTilemap;
    public static Tilemap WalkBehindTilemap => Instance.walkBehindTilemap;
    public static LayerMask ActorLayer => Instance.actorLayer;
    public static float TileSize => Instance.tileSize;
    public static Vector2 GridOffset => new Vector2(0.5f, 0.5f);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
}
