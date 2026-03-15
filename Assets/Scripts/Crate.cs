using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections;
using static GridUtils;
using Unity.VisualScripting;


public class Crate : MonoBehaviour, IGridActor
{    
    private static readonly int PushTrigger = Animator.StringToHash("Push");
    private static readonly int BumpTrigger = Animator.StringToHash("Bump");
    private const float MoveDuration = 0.15f;

    private Rigidbody2D rb;
    private Animator animator;
    private bool isMoving;
    private Vector2Int gridPosition;
    
    public bool OnPlayerMoveInto(Vector2Int direction)
    {
        return TryPush(direction);
    }

    private void Awake()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        gridPosition = WorldToGrid(transform.position);
        rb.position = GridToWorld(gridPosition);
    }
        
    // We try to push the block
    bool TryPush(Vector2Int direction)
    {
        // we check the space behind the block in that direction.
        Vector2Int targetPos = gridPosition + direction;
        IGridActor actor = QueryTile(targetPos, out bool isHardBlocked);

        if (isHardBlocked) 
        {
            // bump logic
            return false;
        }

        if (actor != null && !actor.OnPlayerMoveInto(direction)) 
        {
            return false;
        }

        Vector3 from = GridToWorld(gridPosition);
        Vector3 to = GridToWorld(targetPos);
        gridPosition = targetPos;
        StartCoroutine(PushRoutine(from, to));
        return true;
    }

    private IEnumerator PushRoutine(Vector3 from, Vector3 to)
    {
        isMoving = true;
        if (animator != null) animator.SetTrigger(PushTrigger);

        float elapsed = 0f;
        while (elapsed < MoveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / MoveDuration);
            rb.MovePosition(Vector3.Lerp(from, to, t));
            yield return null;
        }

        rb.MovePosition(to);
        isMoving = false;
    }
}
