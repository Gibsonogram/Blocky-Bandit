using System.Collections;
using UnityEngine;
using static GridUtils;

public static class ActorUtils
{
    private const float BumpDistance = 0.25f;

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
