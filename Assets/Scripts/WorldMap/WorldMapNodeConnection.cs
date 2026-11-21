using UnityEngine;

// One line of the world map's graph. The nodes form a loop, so each node owns two of these - one
// pointing at the node before it in the path and one at the node after - and each line is
// coloured by the state of the world it leads to: red while that world is still locked, and the
// ordinary colour once it can be entered.
//
// Like the rest of the node's art this is a plain sprite on the node's own sorting layer - no UI
// canvas, and nothing that leans on the pixel perfect camera. It is authored with no sprite and
// handed a generated pixel at runtime, and it renders below the node's circle so a segment reads
// as meeting the circle's edge rather than crossing the middle of the node.
[RequireComponent(typeof(SpriteRenderer))]
public class WorldMapNodeConnection : MonoBehaviour
{
    private const int PixelTextureSize = 4;

    private static Sprite generatedPixelSprite;

    [Tooltip("World-space thickness of the line.")]
    [SerializeField] private float thickness = 0.1f;

    [Tooltip("Colour of a line leading to a world the player can enter.")]
    [SerializeField] private Color lineColor = Color.white;

    [Tooltip("Colour of a line leading to a world that is still locked.")]
    [SerializeField] private Color lockedLineColor = new Color(0.85f, 0.15f, 0.15f, 1f);

    // Below the circle (-1) so the node masks the innermost pixels of the line, and with the
    // locked lines one step above the open ones: both nodes draw the same segment, so the locked
    // copy has to win rather than depending on which node happened to draw last.
    [SerializeField] private int lineSortingOrder = -3;
    [SerializeField] private int lockedSortingOrderOffset = 1;

    // Assigned by the owning node, which is told its neighbours by the navigator.
    private WorldMapNode target;

    // Set by the navigator, so the whole graph can be faded from one place.
    private float opacity = 1f;

    private SpriteRenderer lineRenderer;
    // The nodes never move on their own, so the line is only rebuilt when something it depends on
    // actually changed: the entering pulse and the locked feedback both tween the node's transform.
    private Vector3 lastOwnerPosition;
    private Vector3 lastOwnerScale;
    private Vector3 lastTargetPosition;
    private bool lastTargetUnlocked;
    private bool needsRedraw = true;

    private void Awake()
    {
        lineRenderer = GetComponent<SpriteRenderer>();
        EnsurePixelSprite();
    }

    private void LateUpdate()
    {
        Transform owner = transform.parent;
        if (owner == null)
            return;

        Vector3 ownerPosition = owner.position;
        Vector3 ownerScale = owner.lossyScale;
        Vector3 targetPosition = target != null ? target.transform.position : Vector3.zero;
        bool targetUnlocked = target != null && target.IsUnlocked;

        if (!needsRedraw &&
            ownerPosition == lastOwnerPosition &&
            ownerScale == lastOwnerScale &&
            targetPosition == lastTargetPosition &&
            targetUnlocked == lastTargetUnlocked)
            return;

        lastOwnerPosition = ownerPosition;
        lastOwnerScale = ownerScale;
        lastTargetPosition = targetPosition;
        lastTargetUnlocked = targetUnlocked;
        needsRedraw = false;

        Redraw(owner, ownerScale, targetPosition, targetUnlocked);
    }

    // Called by the node when the navigator tells it which neighbours it has. A null target means
    // the node has no connection that way, and the line hides itself.
    public void SetTarget(WorldMapNode newTarget)
    {
        target = newTarget;
        needsRedraw = true;
    }

    // The map's global line alpha, applied on top of whichever colour the line ends up using.
    public void SetOpacity(float value)
    {
        opacity = Mathf.Clamp01(value);
        needsRedraw = true;
    }

    private void Redraw(Transform owner, Vector3 ownerScale, Vector3 targetPosition, bool targetUnlocked)
    {
        if (lineRenderer == null)
            return;

        if (target == null)
        {
            lineRenderer.enabled = false;
            return;
        }

        if (!lineRenderer.enabled)
            lineRenderer.enabled = true;

        float scale = Mathf.Max(0.0001f, Mathf.Abs(ownerScale.y));

        // Built in the node's own space: the endpoints have to stay on the two node centres no
        // matter what the node's own tween is currently doing to its scale.
        Vector3 localEnd = owner.InverseTransformPoint(targetPosition);
        Vector3 delta = targetPosition - owner.position;

        transform.localPosition = localEnd * 0.5f;
        transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f);
        // The sprite is one unit wide and tall, so the scale is the world size divided by whatever
        // the node is currently scaled to.
        transform.localScale = new Vector3(thickness / scale, localEnd.magnitude, 1f);

        Color color = targetUnlocked ? lineColor : lockedLineColor;
        color.a *= opacity;
        if (lineRenderer.color != color)
            lineRenderer.color = color;

        int order = targetUnlocked ? lineSortingOrder : lineSortingOrder + lockedSortingOrderOffset;
        if (lineRenderer.sortingOrder != order)
            lineRenderer.sortingOrder = order;
    }

    private void EnsurePixelSprite()
    {
        if (lineRenderer == null || lineRenderer.sprite != null)
            return;

        if (generatedPixelSprite == null)
            generatedPixelSprite = CreatePixelSprite();

        lineRenderer.sprite = generatedPixelSprite;
    }

    // A one-unit white square, tinted by the renderer's colour and sampled with point filtering so
    // the line keeps a crisp edge at any length.
    private static Sprite CreatePixelSprite()
    {
        Texture2D texture = new Texture2D(PixelTextureSize, PixelTextureSize, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[PixelTextureSize * PixelTextureSize];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32(255, 255, 255, 255);

        texture.SetPixels32(pixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, PixelTextureSize, PixelTextureSize), new Vector2(0.5f, 0.5f), PixelTextureSize);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
