using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static GridUtils;

public static class ActorUtils
{
    private const float BumpDistance = 0.25f;

    // Walks the entire push chain starting one tile ahead of currentGridPos.
    // If the chain terminates in an empty tile: all actors execute their push and returns true.
    // If hard-blocked or a non-pushable actor is found: all collected actors execute a bump and returns false.
    public static bool TryResolvePush(Vector2Int currentGridPos, Vector2Int direction, out Vector2Int nextGridPosition)
    {
        nextGridPosition = currentGridPos;
        var chain = new List<IPushable>();
        Vector2Int scan = currentGridPos + direction;

        while (true)
        {
            IGridActor actor = QueryTile(scan, out bool isHardBlocked);

            if (isHardBlocked)
            {
                foreach (var a in chain) a.ExecuteBump(direction);
                return false;
            }

            if (actor == null) break; // empty tile — chain is valid

            if (actor is IPushable pushable)
            {
                chain.Add(pushable);
                scan += direction;
            }
            else
            {
                foreach (var a in chain) a.ExecuteBump(direction);
                return false;
            }
        }

        foreach (var pushable in chain)
            pushable.ExecutePush(direction);

        nextGridPosition = currentGridPos + direction;
        return true;
    }

    public static IEnumerator BumpCoroutine(Rigidbody2D rb, Vector2Int gridPosition, Vector2Int direction, float duration)
    {
        Vector3 from = GridToWorld(gridPosition);
        Vector3 bumpTarget = from + new Vector3(direction.x, direction.y) * BumpDistance;
        float half = duration / 2f;
        float elapsed = 0f;

        while (elapsed < half)
        {
            elapsed += Time.fixedDeltaTime;
            rb.MovePosition(Vector3.Lerp(from, bumpTarget, Mathf.Clamp01(elapsed / half)));
            yield return new WaitForFixedUpdate();
        }

        elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.fixedDeltaTime;
            rb.MovePosition(Vector3.Lerp(bumpTarget, from, Mathf.Clamp01(elapsed / half)));
            yield return new WaitForFixedUpdate();
        }

        rb.MovePosition(from);
    }
}
