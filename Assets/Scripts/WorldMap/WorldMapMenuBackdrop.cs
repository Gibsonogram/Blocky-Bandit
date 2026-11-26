using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

// The main-menu backdrop for the world map.
//
// While the main menu is open over the map, the map's own dressing (nodes, labels, connection
// lines, the player marker) fades away so none of it is drawn under the menu. Actor scenery
// (the ActorIdleVisual stand-ins) is left alone: those actors stay on screen and keep animating.
// After a short delay the camera leaves the player and tours the map around a closed, smoothed
// cubic B-spline built from a freshly generated random route through the points. The camera's
// current position is inserted as the tour's first waypoint, so the tour begins where the camera
// already is; that same route then repeats until the menu closes.
//
// The camera is never moved directly: PersistentCameraController is asked to follow a temporary
// roam target instead of the player, and the scene's own framing (FillScreen, confiner) is left
// untouched. Closing the menu recenters on the player before the rig takes the follow back.
public class WorldMapMenuBackdrop : MonoBehaviour
{
    [Header("Camera Roam")]
    [Tooltip("World-space points the menu camera tours. Keep them inside the map's CameraConfiner.")]
    [SerializeField] private Vector2[] roamPoints =
    {
        new Vector2(-11f, -8f), new Vector2(-6f, -12f), new Vector2(0f, -9f),
        new Vector2(6f, -13f), new Vector2(11f, -6f), new Vector2(10f, 2f),
        new Vector2(6f, 10f), new Vector2(0f, 13f), new Vector2(-6f, 9f),
        new Vector2(-11f, 3f)
    };

    [Tooltip("Seconds the main menu waits before the camera starts moving.")]
    [SerializeField] private float roamDelay = 5f;

    [Tooltip("Seconds one full tour through all points takes.")]
    [SerializeField] private float roamLoopDuration = 30f;

    [Tooltip("Seconds to settle from the camera's exact starting position onto the generated " +
             "curve. The tour's first waypoint is that position, so this is a tiny move.")]
    [SerializeField] private float roamEntryDuration = 1f;

    [Tooltip("Corner-cutting passes applied to the generated route before the spline. 0 follows " +
             "the points exactly (sharpest turns), 2-4 is the smooth menu tour (points shift a few units).")]
    [SerializeField, Range(0, 6)] private int loopSmoothingPasses = 2;

    [Tooltip("Seconds the camera takes to settle back on the player when the menu closes.")]
    [SerializeField] private float recenterDuration = 0.6f;

    [Header("Map Fade")]
    [Tooltip("Seconds the map's nodes and labels take to fade away or back in.")]
    [SerializeField] private float mapFadeDuration = 0.35f;

    private readonly List<SpriteRenderer> sprites = new List<SpriteRenderer>();
    private readonly List<bool> spriteEnabled = new List<bool>();
    private readonly List<float> spriteAlphas = new List<float>();

    private readonly List<TMP_Text> labels = new List<TMP_Text>();
    private readonly List<bool> labelEnabled = new List<bool>();
    private readonly List<float> labelAlphas = new List<float>();

    private readonly List<LineRenderer> lines = new List<LineRenderer>();
    private readonly List<bool> lineEnabled = new List<bool>();
    private readonly List<Color> lineStartColors = new List<Color>();
    private readonly List<Color> lineEndColors = new List<Color>();

    private Transform roamTarget;
    private Coroutine roamRoutine;
    private Coroutine recenterRoutine;
    private Tween fadeTween;
    private bool menuOpen;
    private bool roaming;
    private bool visualsCached;

    // Cached closed tour, rebuilt whenever the menu opens: dense samples of a centripetal
    // Catmull-Rom loop plus the arc length at each sample, so the camera moves at a steady
    // speed around a curve that has no corners (C1 at every control point, including the seam).
    private const int LoopSamplesPerSegment = 48;
    private readonly List<Vector3> loopSamples = new List<Vector3>();
    private readonly List<float> loopDistances = new List<float>();
    private float loopLength;

    // --- called by MainMenuUI --------------------------------------------------------------

    public void EnterMenuMode()
    {
        if (menuOpen)
            return;

        menuOpen = true;
        CancelRecenter();
        StopRoam();

        roamRoutine = StartCoroutine(EnterRoutine());
    }

    // One frame after the request, so the world map's own Start pass has settled which parts
    // of each node it is showing before those visuals are collected for the fade.
    private IEnumerator EnterRoutine()
    {
        yield return null;

        if (!menuOpen)
            yield break;

        CacheVisuals();
        SetMapVisible(false);

        yield return RoamRoutine();
    }

    public void ExitMenuMode()
    {
        if (!menuOpen && !roaming)
            return;

        menuOpen = false;
        StopRoam();
        SetMapVisible(true);

        CancelRecenter();
        recenterRoutine = StartCoroutine(RecenterRoutine());
    }

    // --- camera tour -----------------------------------------------------------------------

    private IEnumerator RoamRoutine()
    {
        PersistentCameraController rig = PersistentCameraController.Instance;
        if (rig == null || roamPoints == null || roamPoints.Length < 3)
            yield break;

        if (roamDelay > 0f)
            yield return new WaitForSeconds(roamDelay);

        if (!menuOpen)
            yield break;

        if (roamTarget == null)
        {
            GameObject target = new GameObject("MainMenuRoamTarget");
            roamTarget = target.transform;
        }

        Transform player = rig.DefaultFollow;
        Vector3 startPosition = player != null ? player.position : Vector3.zero;

        // Use the rendered camera's own x/y where possible: near the confiner edge the camera is
        // clamped away from the player, and the tour must begin where the camera actually is.
        Camera output = rig.OutputCamera;
        if (output != null)
        {
            startPosition.x = output.transform.position.x;
            startPosition.y = output.transform.position.y;
        }

        roamTarget.position = startPosition;

        rig.SetFollowOverride(roamTarget);
        roaming = true;

        BuildLoop();

        // The generated tour starts at the camera's current position; settle onto the first
        // sampled point of the curve (a fraction of a unit away) at the entry speed, then travel
        // at the tour's constant speed. Direction is chosen per menu open.
        Vector3 entryPoint = loopSamples.Count > 0 ? loopSamples[0] : roamTarget.position;

        // Never dart: the settle takes at least roamEntryDuration and, if the gap is larger than
        // a couple of units (a clamped camera at the map edge), it is slowed to ~2.5 units/s.
        float entryDistance = Vector3.Distance(roamTarget.position, entryPoint);
        float entryDuration = Mathf.Max(roamEntryDuration, entryDistance / 2.5f);

        Tween entry = roamTarget.DOMove(entryPoint, entryDuration)
            .SetEase(Ease.InOutSine)
            .SetUpdate(false);
        yield return entry.WaitForCompletion();

        if (!menuOpen)
            yield break;

        float speed = loopLength / Mathf.Max(1f, roamLoopDuration);
        float direction = Random.value < 0.5f ? 1f : -1f;
        float travelled = 0f;

        while (menuOpen)
        {
            travelled = Mathf.Repeat(travelled + direction * speed * Time.deltaTime, loopLength);
            roamTarget.position = SampleLoop(travelled);
            yield return null;
        }
    }

    // Builds the tour: a random route through the points (randomised nearest neighbour, then
    // 2-opt to remove crossings and long jumps), corner-cut for smoothness, then sampled as a
    // closed cubic B-spline (C2 at every join) by arc length so the camera travels at one speed.
    // The same 20-point tour is reused every loop until the menu closes.
    private void BuildLoop()
    {
        Vector3[] control = SmoothLoop(BuildRandomTour(), 0);
        int count = control.Length;

        loopSamples.Clear();
        loopDistances.Clear();

        for (int segment = 0; segment < count; segment++)
        {
            Vector3 c0 = control[(segment - 1 + count) % count];
            Vector3 c1 = control[segment];
            Vector3 c2 = control[(segment + 1) % count];
            Vector3 c3 = control[(segment + 2) % count];

            for (int step = 0; step < LoopSamplesPerSegment; step++)
            {
                float t = step / (float)LoopSamplesPerSegment;
                Vector3 sample = CubicBSpline(c0, c1, c2, c3, t);

                if (loopSamples.Count == 0)
                {
                    loopSamples.Add(sample);
                    loopDistances.Add(0f);
                    continue;
                }

                loopDistances.Add(loopDistances[loopDistances.Count - 1] +
                                  Vector3.Distance(loopSamples[loopSamples.Count - 1], sample));
                loopSamples.Add(sample);
            }
        }

        // Close the table: the distance from the final sample back to the first is the seam.
        loopDistances.Add(loopDistances[loopDistances.Count - 1] +
                          Vector3.Distance(loopSamples[loopSamples.Count - 1], loopSamples[0]));
        loopSamples.Add(loopSamples[0]);
        loopLength = loopDistances[loopDistances.Count - 1];
    }

    // A random route through every roam point. Nearest-neighbour keeps consecutive points close
    // (a fully random permutation can reverse direction between any two points, which no curve
    // can make fluid), but the next point is chosen between the two closest at random, so the
    // route differs every time. 2-opt then removes the crossings and long jumps that remain.
    private Vector3[] BuildRandomTour()
    {
        float z = roamTarget != null ? roamTarget.position.z : 0f;
        int count = roamPoints.Length;

        Vector3[] tour = new Vector3[count];
        List<int> remaining = new List<int>(count);
        for (int i = 0; i < count; i++)
            remaining.Add(i);

        int first = Random.Range(0, remaining.Count);
        tour[0] = new Vector3(roamPoints[first].x, roamPoints[first].y, z);
        remaining.RemoveAt(first);

        for (int placed = 1; placed < count; placed++)
        {
            Vector3 last = tour[placed - 1];
            int nearest = 0;
            int second = -1;
            float nearestSqr = float.MaxValue;
            float secondSqr = float.MaxValue;

            for (int i = 0; i < remaining.Count; i++)
            {
                Vector2 point = roamPoints[remaining[i]];
                float dx = point.x - last.x;
                float dy = point.y - last.y;
                float sqr = dx * dx + dy * dy;

                if (sqr < nearestSqr)
                {
                    second = nearest;
                    secondSqr = nearestSqr;
                    nearest = i;
                    nearestSqr = sqr;
                }
                else if (sqr < secondSqr)
                {
                    second = i;
                    secondSqr = sqr;
                }
            }

            int chosen = second >= 0 && Random.value < 0.35f ? second : nearest;
            Vector2 next = roamPoints[remaining[chosen]];
            tour[placed] = new Vector3(next.x, next.y, z);
            remaining.RemoveAt(chosen);
        }

        if (Random.value < 0.5f)
            System.Array.Reverse(tour);

        ImproveTour(tour);

        // Start the tour at the camera's current position: insert it into the nearest edge of
        // the loop (a detour, not a spike) and rotate so it becomes the first point.
        return StartTourAt(tour, roamTarget != null ? roamTarget.position : Vector3.zero);
    }

    private static Vector3[] StartTourAt(Vector3[] tour, Vector3 start)
    {
        int count = tour.Length;
        int bestEdge = 0;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            float sqr = SqrDistanceToSegment(start, tour[i], tour[(i + 1) % count]);
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                bestEdge = i;
            }
        }

        List<Vector3> path = new List<Vector3>(count + 1);
        for (int i = 0; i <= count; i++)
        {
            if (i == bestEdge + 1)
                path.Add(start);

            if (i < count)
                path.Add(tour[i]);
        }

        int inserted = bestEdge + 1;
        Vector3[] started = new Vector3[path.Count];
        for (int i = 0; i < started.Length; i++)
            started[i] = path[(inserted + i) % path.Count];

        return started;
    }

    private static float SqrDistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lengthSqr = ab.sqrMagnitude;
        if (lengthSqr <= 0.0001f)
            return (point - a).sqrMagnitude;

        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSqr);
        Vector3 closest = a + ab * t;
        return (point - closest).sqrMagnitude;
    }

    // Closed-tour 2-opt: while reversing a segment shortens the loop, do it. This removes the
    // long "return home" jump and the crossings a nearest-neighbour route can leave behind.
    private static void ImproveTour(Vector3[] tour)
    {
        int count = tour.Length;
        if (count < 4)
            return;

        for (int pass = 0; pass < 20; pass++)
        {
            bool improved = false;

            for (int i = 0; i < count - 1; i++)
            {
                for (int j = i + 2; j < count; j++)
                {
                    if (i == 0 && j == count - 1)
                        continue;

                    Vector3 a = tour[i];
                    Vector3 b = tour[i + 1];
                    Vector3 c = tour[j];
                    Vector3 d = tour[(j + 1) % count];

                    float current = (a - b).sqrMagnitude + (c - d).sqrMagnitude;
                    float candidate = (a - c).sqrMagnitude + (b - d).sqrMagnitude;

                    if (candidate + 0.0001f >= current)
                        continue;

                    System.Array.Reverse(tour, i + 1, j - i);
                    improved = true;
                }
            }

            if (!improved)
                break;
        }
    }

    // Constant-speed lookup: binary search the arc-length table, then interpolate between the two
    // dense samples either side of the requested distance. Sampling by distance (not by curve
    // parameter) is what keeps the speed even across long and short segments.
    private Vector3 SampleLoop(float distance)
    {
        if (loopSamples.Count < 2 || loopLength <= 0f)
            return roamTarget != null ? roamTarget.position : Vector3.zero;

        distance = Mathf.Repeat(distance, loopLength);

        int low = 0;
        int high = loopDistances.Count - 1;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (loopDistances[mid] < distance) low = mid + 1;
            else high = mid;
        }

        int index = Mathf.Max(1, low);
        float span = loopDistances[index] - loopDistances[index - 1];
        float t = span > 0f ? (distance - loopDistances[index - 1]) / span : 0f;
        return Vector3.Lerp(loopSamples[index - 1], loopSamples[index], t);
    }

    // Corner cutting: p = p/2 + (previous + next)/4 around the closed route, repeated for the
    // configured number of passes. pinIndex is the camera's own starting point: it is put back
    // after every pass so the tour still begins exactly there. At 0 passes the spline follows
    // the authored points exactly.
    private Vector3[] SmoothLoop(Vector3[] loop, int pinIndex)
    {
        Vector3 pinned = loop[pinIndex];
        Vector3[] current = loop;

        for (int pass = 0; pass < loopSmoothingPasses; pass++)
        {
            Vector3[] next = new Vector3[current.Length];
            for (int i = 0; i < current.Length; i++)
            {
                Vector3 previous = current[(i - 1 + current.Length) % current.Length];
                Vector3 following = current[(i + 1) % current.Length];
                next[i] = current[i] * 0.5f + (previous + following) * 0.25f;
            }

            next[pinIndex] = pinned;
            current = next;
        }

        return current;
    }

    // Uniform cubic B-spline segment through four control points. The curve is C2 across every
    // join (and across the loop seam, because the control points wrap), so the camera never
    // changes curvature abruptly.
    private static Vector3 CubicBSpline(Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return ((1f - t) * (1f - t) * (1f - t) * c0
                + (3f * t3 - 6f * t2 + 4f) * c1
                + (-3f * t3 + 3f * t2 + 3f * t + 1f) * c2
                + t3 * c3) / 6f;
    }

    private IEnumerator RecenterRoutine()
    {
        PersistentCameraController rig = PersistentCameraController.Instance;
        Transform player = rig != null ? rig.DefaultFollow : null;

        if (roaming && roamTarget != null && player != null)
        {
            roamTarget.DOKill();
            yield return roamTarget.DOMove(player.position, Mathf.Max(0f, recenterDuration))
                .SetEase(Ease.InOutSine)
                .SetUpdate(false)
                .WaitForCompletion();
        }

        if (rig != null)
            rig.ClearFollowOverride();

        roaming = false;
        recenterRoutine = null;
    }

    private void StopRoam()
    {
        if (roamRoutine != null)
        {
            StopCoroutine(roamRoutine);
            roamRoutine = null;
        }
    }

    private void CancelRecenter()
    {
        if (recenterRoutine != null)
        {
            StopCoroutine(recenterRoutine);
            recenterRoutine = null;
        }
    }

    // --- map fade --------------------------------------------------------------------------

    // Recollected on every menu open rather than cached once: nodes change which parts of
    // themselves they show as worlds unlock, so the set of visible renderers is not fixed.
    private void CacheVisuals()
    {
        sprites.Clear();
        spriteEnabled.Clear();
        spriteAlphas.Clear();
        labels.Clear();
        labelEnabled.Clear();
        labelAlphas.Clear();
        lines.Clear();
        lineEnabled.Clear();
        lineStartColors.Clear();
        lineEndColors.Clear();

        UnityEngine.SceneManagement.Scene scene = gameObject.scene;

        foreach (SpriteRenderer sprite in FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (sprite == null || sprite.gameObject.scene != scene || !sprite.gameObject.activeInHierarchy)
                continue;

            // Actor scenery (the ActorIdleVisual stand-ins placed around the map) is part of the
            // menu's backdrop: it keeps animating and stays on screen instead of fading out with
            // the map's nodes and labels. The player marker is not one of these, so it still fades.
            if (sprite.GetComponentInParent<ActorIdleVisual>() != null)
                continue;

            sprites.Add(sprite);
            spriteEnabled.Add(sprite.enabled);
            spriteAlphas.Add(sprite.color.a);
        }

        foreach (TMP_Text label in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (label == null || label.gameObject.scene != scene || !label.gameObject.activeInHierarchy)
                continue;

            labels.Add(label);
            labelEnabled.Add(label.enabled);
            labelAlphas.Add(label.alpha);
        }

        foreach (LineRenderer line in FindObjectsByType<LineRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (line == null || line.gameObject.scene != scene || !line.gameObject.activeInHierarchy)
                continue;

            lines.Add(line);
            lineEnabled.Add(line.enabled);
            lineStartColors.Add(line.startColor);
            lineEndColors.Add(line.endColor);
        }

        visualsCached = true;
    }

    private void SetMapVisible(bool visible)
    {
        if (!visualsCached)
            return;

        fadeTween?.Kill();

        if (visible)
        {
            SetVisualsEnabled(true);
            ApplyAlpha(0f);
            fadeTween = DOVirtual.Float(0f, 1f, Mathf.Max(0f, mapFadeDuration), ApplyAlpha)
                .SetEase(Ease.Linear)
                .SetUpdate(false);
            return;
        }

        fadeTween = DOVirtual.Float(1f, 0f, Mathf.Max(0f, mapFadeDuration), ApplyAlpha)
            .SetEase(Ease.Linear)
            .SetUpdate(false)
            .OnComplete(() => SetVisualsEnabled(false));
    }

    private void ApplyAlpha(float alpha)
    {
        for (int i = 0; i < sprites.Count; i++)
        {
            SpriteRenderer sprite = sprites[i];
            if (sprite == null) continue;

            Color color = sprite.color;
            color.a = spriteAlphas[i] * alpha;
            sprite.color = color;
        }

        for (int i = 0; i < labels.Count; i++)
        {
            TMP_Text label = labels[i];
            if (label == null) continue;

            label.alpha = labelAlphas[i] * alpha;
        }

        for (int i = 0; i < lines.Count; i++)
        {
            LineRenderer line = lines[i];
            if (line == null) continue;

            Color start = lineStartColors[i];
            Color end = lineEndColors[i];
            start.a *= alpha;
            end.a *= alpha;
            line.startColor = start;
            line.endColor = end;
        }
    }

    private void SetVisualsEnabled(bool enabled)
    {
        for (int i = 0; i < sprites.Count; i++)
        {
            if (sprites[i] != null)
                sprites[i].enabled = enabled && spriteEnabled[i];
        }

        for (int i = 0; i < labels.Count; i++)
        {
            if (labels[i] != null)
                labels[i].enabled = enabled && labelEnabled[i];
        }

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i] != null)
                lines[i].enabled = enabled && lineEnabled[i];
        }
    }

    private void OnDestroy()
    {
        StopRoam();
        CancelRecenter();
        fadeTween?.Kill();

        if (roamTarget != null)
        {
            roamTarget.DOKill();
            Destroy(roamTarget.gameObject);
            roamTarget = null;
        }

        // The rig outlives this scene; it must not keep chasing a target that is being destroyed.
        if (roaming)
            PersistentCameraController.Instance?.ClearFollowOverride();

        roaming = false;
    }
}
