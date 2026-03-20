using UnityEngine;
using UnityEngine.Tilemaps;
using static GridUtils;

public class PlayerShadow : MonoBehaviour
{

    [SerializeField] private SpriteRenderer playerSpriteRenderer;

    private SpriteRenderer shadowRenderer;
    private PlayerController player;
    private Vector2Int lastPos;

    void Awake()
    {
        // this is the 
        shadowRenderer = GetComponent<SpriteRenderer>();
        player = GetComponentInParent<PlayerController>();
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        player.OnMovementComplete += CheckWalkBehind;
        CheckWalkBehind();
        
    }

    void CheckWalkBehind()
    {
        Vector3Int tilePos = GridSettings.WalkBehindTilemap.WorldToCell(player.transform.position);
        bool isUnder = GridSettings.WalkBehindTilemap.HasTile(tilePos);
        Debug.Log($"CheckWalkBehind fired, tilePos: {tilePos}, hasWalkBehindTile: {isUnder}");

        shadowRenderer.enabled = isUnder;
    }

    void OnDestroy()
    {
        if (player != null)
        {
            player.OnMovementComplete -= CheckWalkBehind;
        }
    }

    // Use late update because we want Update to run Animator on the player sprite
    void LateUpdate()
    {
        shadowRenderer.sprite = playerSpriteRenderer.sprite;
    }
}
