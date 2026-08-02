using DG.Tweening;
using TMPro;
using UnityEngine;

public class WorldMapNode : MonoBehaviour
{
    private const float DefaultSelectedScaleMultiplier = 1.2f;
    private const float DefaultSelectionDuration = 0.15f;
    private const int CircleTextureSize = 32;
    private const float CircleRadius = 15.5f;

    private static Sprite generatedCircleSprite;

    [SerializeField] private WorldData worldData;
    [SerializeField] private SpriteRenderer baseCircle;
    [SerializeField] private GameObject lockedDisplay;
    [SerializeField] private GameObject lockedRow;
    [SerializeField] private SpriteRenderer lockSprite;
    [SerializeField] private TextMeshPro progressText;
    [SerializeField] private SpriteRenderer collectableIcon;
    [SerializeField] private GameObject unlockedDisplay;
    [SerializeField] private float selectedScaleMultiplier = DefaultSelectedScaleMultiplier;
    [SerializeField] private float selectionDuration = DefaultSelectionDuration;
    [SerializeField] private Ease selectionEase = Ease.OutBack;

    private Vector3 baseScale;
    private Tween scaleTween;

    public WorldData WorldData => worldData;

    private void Awake()
    {
        baseScale = transform.localScale;
        EnsureBaseCircleSprite();
    }

    private void OnEnable()
    {
        if (SaveManager.Instance != null)
            RefreshPresentation();
    }

    private void Start()
    {
        RefreshPresentation();
    }

    private void OnDisable()
    {
        scaleTween?.Kill();
        scaleTween = null;
        transform.localScale = baseScale;
    }

    public void RefreshPresentation()
    {
        if (worldData == null)
        {
            Debug.LogWarning($"World Map node '{name}' has no WorldData assigned.", this);
            SetDisplayState(showLocked: true, 0, 0);
            return;
        }

        int threshold = worldData.collectableUnlockThreshold;
        if (threshold < 0)
        {
            Debug.LogWarning($"World Map node '{name}' has an invalid negative unlock threshold.", this);
            SetDisplayState(showLocked: true, 0, 0);
            return;
        }

        if (SaveManager.Instance == null)
            return;

        int totalCollectables = SaveManager.Instance.GetTotalCollectables();
        SetDisplayState(totalCollectables < threshold, totalCollectables, threshold);
    }

    public void SetSelected(bool isSelected)
    {
        scaleTween?.Kill();
        Vector3 targetScale = isSelected
            ? baseScale * Mathf.Max(0f, selectedScaleMultiplier)
            : baseScale;

        scaleTween = transform
            .DOScale(targetScale, Mathf.Max(0f, selectionDuration))
            .SetEase(selectionEase)
            .SetUpdate(false);
    }

    private void SetDisplayState(bool showLocked, int totalCollectables, int threshold)
    {
        if (lockedDisplay == null || lockedRow == null || unlockedDisplay == null || progressText == null || lockSprite == null || collectableIcon == null || baseCircle == null)
            Debug.LogWarning($"World Map node '{name}' is missing one or more presentation references.", this);

        if (lockedDisplay != null)
            lockedDisplay.SetActive(showLocked);

        if (lockedRow != null)
            lockedRow.SetActive(showLocked);

        if (unlockedDisplay != null)
            unlockedDisplay.SetActive(!showLocked);

        if (!showLocked)
            return;

        if (progressText != null)
        {
            progressText.text = $"{Mathf.Max(0, totalCollectables)}/{Mathf.Max(0, threshold)}";

            if (lockSprite != null)
            {
                progressText.sortingLayerID = lockSprite.sortingLayerID;
                progressText.sortingOrder = lockSprite.sortingOrder + 1;
            }
        }

        if (lockSprite != null)
            lockSprite.enabled = true;

        if (collectableIcon != null)
            collectableIcon.enabled = true;
    }

    private void EnsureBaseCircleSprite()
    {
        if (baseCircle == null || baseCircle.sprite != null)
            return;

        if (generatedCircleSprite == null)
            generatedCircleSprite = CreateCircleSprite();

        baseCircle.sprite = generatedCircleSprite;
    }

    private static Sprite CreateCircleSprite()
    {
        Texture2D texture = new Texture2D(CircleTextureSize, CircleTextureSize, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[CircleTextureSize * CircleTextureSize];
        float center = (CircleTextureSize - 1) * 0.5f;
        float radiusSquared = CircleRadius * CircleRadius;

        for (int y = 0; y < CircleTextureSize; y++)
        {
            for (int x = 0; x < CircleTextureSize; x++)
            {
                float horizontalDistance = x - center;
                float verticalDistance = y - center;
                bool isInsideCircle = horizontalDistance * horizontalDistance + verticalDistance * verticalDistance <= radiusSquared;
                pixels[y * CircleTextureSize + x] = isInsideCircle
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(0, 0, 0, 0);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

        Sprite circleSprite = Sprite.Create(texture, new Rect(0, 0, CircleTextureSize, CircleTextureSize), new Vector2(0.5f, 0.5f), CircleTextureSize);
        circleSprite.hideFlags = HideFlags.HideAndDontSave;
        return circleSprite;
    }
}
