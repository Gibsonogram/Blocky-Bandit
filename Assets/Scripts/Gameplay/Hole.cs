using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using static GridUtils;

// A static grid hazard. Never moves, never blocks pathing or vision (no collider, off
// ActorLayer). Any actor that lands on or slides over its tile is consumed: the victim's
// own sprite falls into the tile while shrinking and fading, then is destroyed. No corpse.
public class Hole : MonoBehaviour
{
    [SerializeField] private SpriteRenderer holeSprite;

    [Header("Fall Tuning")]
    [SerializeField] private float fallDuration = 0.8f;

    private const float EndScale = 0.5f;
    private const float EndAlpha = 0.5f;
    private Vector2Int gridPosition;

    private void Start()
    {
        gridPosition = WorldToGrid(transform.position);
        transform.position = GridToWorld(gridPosition);
        HoleRegistry.Register(this, gridPosition);
    }

    private void OnDestroy()
    {
        HoleRegistry.Unregister(gridPosition);
    }


    public void Consume(GameObject victim, bool isPlayer)
    {
        SpriteRenderer victimSprite = victim.GetComponentInChildren<SpriteRenderer>();
        // Dispose of the victim's logic before animating the detached visual.
        
        if (!isPlayer)
        {
            if (victim.TryGetComponent(out IVisionSource visionSource))
                VisionOverlayRenderer.Instance?.UnregisterSource(visionSource);
        }

        // Freeze whatever animation the sprite was playing.
        Animator victimAnimator = victimSprite.GetComponentInParent<Animator>();
        if (victimAnimator != null)
            victimAnimator.enabled = false;
        
        // Detach the transform so that our little anim can play out unperterbed.
        Transform visualTransform = victimSprite.transform;
        if (isPlayer)
            visualTransform = visualTransform.parent;
        visualTransform.SetParent(null, true);

        // Snap X/Y to the hole center for a clean fall.
        Vector3 holeCenter = GridToWorld(gridPosition);
        visualTransform.position = new Vector3(holeCenter.x, holeCenter.y, visualTransform.position.z);
        
        if (!isPlayer)
        {
            StartCoroutine(FallRoutine(visualTransform, victimSprite));
            Destroy(victim);   
        }
        else
        {
            // this ensures the action GAMEOVER happens here but only after fall-routine.
            StartCoroutine(FallRoutine(visualTransform, victimSprite, () => PauseUI.Trigger(PauseContext.GameOver)));
        }
    }

    private IEnumerator FallRoutine(Transform visualTransform, SpriteRenderer sprite, Action onComplete = null)
    {
        Vector3 startPos = visualTransform.position;
        Vector3 endPos = startPos + Vector3.down * GridSettings.TileSize;
        Vector3 startScale = visualTransform.localScale;
        Vector3 endScale = startScale * EndScale;
        Color startColor = sprite.color;

        float elapsed = 0f;
        while (elapsed < fallDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fallDuration);
            visualTransform.position = Vector3.Lerp(startPos, endPos, t);
            visualTransform.localScale = Vector3.Lerp(startScale, endScale, t);
            Color color = startColor;
            color.a = Mathf.Lerp(startColor.a, EndAlpha, t);
            sprite.color = color;
            yield return null;
        }

        Destroy(visualTransform.gameObject);
        onComplete?.Invoke();
    }
}
