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

    [SerializeField] private SpriteMask fallMask;
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
        SpriteMaskInteraction originalMaskInteraction = victimSprite.maskInteraction;
        victimSprite.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        // Dispose of the victim's logic before animating the detached visual.
        if (!isPlayer)
            RetireVictim(victim);

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
            // The outcome was already locked by PlayerController the instant the hole was
            // spotted; this just presents it once the fall animation actually lands.
            StartCoroutine(FallRoutine(visualTransform, victimSprite, () => LevelEndUI.ShowGameOver()));
        }
    }

    // Takes the victim off every logical board read before the fall animation plays. Destroy()
    // is deferred to the end of the frame, so the victim's own OnDestroy would otherwise keep
    // its tile occupied until after this turn's vision refresh - a cone the hole just opened
    // would stay blocked until the next turn. The overlay is repainted here, once the tile is
    // actually free, so this same turn already reflects the missing body (and enemies can
    // switch to chase if the opened line now shows the player).
    private void RetireVictim(GameObject victim)
    {
        if (victim.TryGetComponent(out IVisionSource visionSource))
            VisionOverlayRenderer.Instance?.UnregisterSource(visionSource);

        if (victim.TryGetComponent(out ITurnActor turnActor))
            TurnManager.Instance?.UnregisterActor(turnActor);

        // A consumed actor is committed to the hole's tile, so that is the entry to free.
        if (victim.TryGetComponent(out IGridActor gridActor) && gridActor is Component occupant)
            GridOccupancy.Unregister(gridPosition, occupant);

        TurnManager.Instance?.NotifyBoardChanged();
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
