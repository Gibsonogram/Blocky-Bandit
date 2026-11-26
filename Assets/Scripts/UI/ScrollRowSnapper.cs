using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Keeps the level list resting on whole rows.
//
// A ScrollRect stops wherever the pointer, the wheel or the inertia left it, so the list can come to
// rest with a button sliced across the top or bottom edge - which is exactly the slice that makes a
// player lose track of which row their gamepad is on. The rows are a fixed distance apart (row
// height plus the layout group's spacing) and the window is not a whole number of rows tall, so the
// resting position is what decides how much of the next row shows. This component does two jobs:
//
//   - it snaps the resting position to a row boundary, so the window always shows whole rows plus a
//     sliver of the next one - the "peek" that says the list continues below;
//   - it owns the focus scroll, so a row picked with the D-pad lands on a boundary in one motion
//     instead of being nudged into view and then slid somewhere else by the snap.
//
// Snapping only happens once the list has settled: a drag, its inertia and a wheel flick are never
// fought mid-motion. The step is measured from the content, so a change to the row prefab or the
// layout spacing carries over without touching this component.
[RequireComponent(typeof(ScrollRect))]
public class ScrollRowSnapper : MonoBehaviour, IBeginDragHandler, IEndDragHandler
{
    [Tooltip("Seconds the list has to sit still before it snaps, so a drag or a wheel flick is never fought mid-motion.")]
    [SerializeField] private float settleDelay = 0.08f;
    [Tooltip("Seconds the snap to a row boundary takes.")]
    [SerializeField] private float snapDuration = 0.14f;
    [SerializeField] private Ease snapEase = Ease.OutQuad;
    [Tooltip("Distance from a row boundary that already counts as snapped.")]
    [SerializeField] private float snapTolerance = 0.5f;
    [Tooltip("Move one whole row per mouse wheel notch, so the wheel and the D-pad agree.")]
    [SerializeField] private bool oneWheelNotchPerRow = true;

    private ScrollRect scrollRect;
    private float rowStep;
    private int cachedChildCount = -1;
    private float cachedContentHeight = -1f;
    private Vector2 lastPosition;
    private float settleTimer;
    private Tween snapTween;
    private bool isDragging;

    private RectTransform Viewport => scrollRect != null && scrollRect.viewport != null
        ? scrollRect.viewport
        : (RectTransform)transform;

    private RectTransform Content => scrollRect != null ? scrollRect.content : null;

    // How far the content can travel: 0 when every row already fits, which is also the "nothing to
    // snap" case.
    private float ScrollRange
    {
        get
        {
            RectTransform content = Content;
            RectTransform viewport = Viewport;
            if (content == null || viewport == null)
                return 0f;

            return Mathf.Max(0f, content.rect.height - viewport.rect.height);
        }
    }

    private void Awake()
    {
        scrollRect = GetComponent<ScrollRect>();
    }

    private void OnEnable()
    {
        lastPosition = Content != null ? Content.anchoredPosition : Vector2.zero;
        settleTimer = 0f;
        CacheMetrics(force: true);
    }

    private void OnDisable()
    {
        KillSnap();
    }

    private void LateUpdate()
    {
        if (Content == null || Viewport == null)
            return;

        CacheMetrics(force: false);
        if (rowStep <= 0f)
            return;

        // A running snap owns the position until it lands; a drag owns it until release.
        if (snapTween != null && snapTween.IsActive())
            return;

        Vector2 position = Content.anchoredPosition;
        if (isDragging)
        {
            lastPosition = position;
            settleTimer = 0f;
            return;
        }

        // Any movement - inertia, a wheel notch, another script scrolling the list - restarts the
        // settle timer, so the snap only fires once the list has actually stopped.
        if ((position - lastPosition).sqrMagnitude > 0.01f || scrollRect.velocity.sqrMagnitude > 1f)
        {
            lastPosition = position;
            settleTimer = 0f;
            return;
        }

        settleTimer += Time.unscaledDeltaTime;
        if (settleTimer < settleDelay)
            return;

        float target = NearestBoundary(position.y);
        if (Mathf.Abs(target - position.y) <= snapTolerance)
            return;

        SnapTo(target);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        isDragging = true;
        settleTimer = 0f;
        KillSnap();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;
        settleTimer = 0f;
    }

    // Brings a row fully into the window and leaves the list on a boundary. Called by the level list
    // when the focus ring (keyboard/gamepad) moves, because UGUI moves selection without touching
    // the ScrollRect.
    public void EnsureVisible(RectTransform item, float padding)
    {
        RectTransform content = Content;
        RectTransform viewport = Viewport;
        if (content == null || viewport == null || item == null)
            return;

        CacheMetrics(force: true);
        if (rowStep <= 0f)
            return;

        // Buttons are placed by a VerticalLayoutGroup, so their positions are only trustworthy once
        // the layout has been rebuilt after a populate.
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, item);
        Rect window = viewport.rect;
        Vector2 position = content.anchoredPosition;

        // The positions at which the row is fully inside the window, with the caller's padding kept
        // clear of both edges. Content is top-anchored, so a larger y means the list has scrolled
        // further down and the row sits higher on screen.
        float clampedPadding = Mathf.Max(0f, padding);
        float lowest = position.y + (window.yMin + clampedPadding - bounds.min.y);
        float highest = position.y + (window.yMax - clampedPadding - bounds.max.y);

        float range = ScrollRange;
        lowest = Mathf.Clamp(lowest, 0f, range);
        highest = Mathf.Clamp(highest, 0f, range);
        if (lowest > highest)
        {
            // The row is taller than the window: align its top and let the bottom clip.
            lowest = highest;
        }

        float target = Mathf.Clamp(NearestBoundary(position.y), lowest, highest);
        if (Mathf.Abs(target - position.y) <= snapTolerance)
            return;

        SnapTo(target);
    }

    // Called when the screen opens, so the next world starts at the top instead of wherever the
    // previous list came to rest.
    public void ResetToTop()
    {
        KillSnap();
        settleTimer = 0f;

        if (scrollRect != null)
            scrollRect.StopMovement();

        if (Content != null)
            Content.anchoredPosition = Vector2.zero;

        lastPosition = Vector2.zero;
    }

    private float NearestBoundary(float position)
    {
        if (rowStep <= 0f)
            return position;

        return Mathf.Clamp(Mathf.Round(position / rowStep) * rowStep, 0f, ScrollRange);
    }

    private void SnapTo(float target)
    {
        KillSnap();
        scrollRect.StopMovement();
        settleTimer = 0f;
        snapTween = Content.DOAnchorPosY(target, snapDuration).SetEase(snapEase).SetUpdate(true);
    }

    private void KillSnap()
    {
        if (snapTween == null)
            return;

        snapTween.Kill();
        snapTween = null;
    }

    // The step only changes when the list is populated (child count) or the content is measured,
    // so it is re-read on those two events rather than every frame.
    private void CacheMetrics(bool force)
    {
        RectTransform content = Content;
        if (content == null)
            return;

        int childCount = content.childCount;
        float contentHeight = content.rect.height;
        if (!force && childCount == cachedChildCount && Mathf.Approximately(contentHeight, cachedContentHeight))
            return;

        cachedChildCount = childCount;
        cachedContentHeight = contentHeight;
        rowStep = MeasureRowStep();

        // The wheel steps by one row so it agrees with the focus ring; ScrollRect multiplies the
        // notch by this value.
        if (oneWheelNotchPerRow && rowStep > 0f && scrollRect != null)
            scrollRect.scrollSensitivity = rowStep;
    }

    // Row height plus the layout group's spacing, i.e. the distance between two rows' top edges.
    private float MeasureRowStep()
    {
        RectTransform content = Content;
        if (content == null)
            return 0f;

        float spacing = 0f;
        if (content.TryGetComponent(out VerticalLayoutGroup layout))
            spacing = layout.spacing;

        for (int i = 0; i < content.childCount; i++)
        {
            if (!(content.GetChild(i) is RectTransform row))
                continue;

            float height = row.rect.height;
            if (height <= 0f && row.TryGetComponent(out LayoutElement element))
                height = element.preferredHeight;

            if (height > 0f)
                return height + spacing;
        }

        return 0f;
    }
}
