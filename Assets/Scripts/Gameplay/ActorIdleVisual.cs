using System.Collections;
using UnityEngine;

// Presentation-only stand-in for a gameplay actor. It plays the actor's idle look - the small
// sine bob a rook rides, the sleep-frame loop a bishop breathes through - and nothing else: no
// turn registration, no grid occupancy, no chase and no danger overlay.
//
// Scenes that stage actors as scenery instead of as enemies (the world map, for one) place a
// variant of the actor prefab with this component swapped in for the real one, so the scene
// shows the art without any invisible AI running behind it.
public class ActorIdleVisual : MonoBehaviour
{
    [Header("Bob")]
    [Tooltip("Transform that rides the idle bob. Leave empty for an actor that only animates frames.")]
    [SerializeField] private Transform bobTarget;
    [SerializeField] private float bobAmount = 0.04f;
    [SerializeField] private float bobSpeed = 5f;

    [Header("Frames")]
    [Tooltip("Sprites looped for the idle animation. Leave empty for an actor that only bobs.")]
    [SerializeField] private Sprite[] idleFrames;
    [SerializeField] private float frameDuration = 0.5f;

    private SpriteRenderer spriteRenderer;
    private Vector3 bobBase;
    private bool hasBobTarget;

    private void Awake()
    {
        // The sprite sits on the child the bob rides, exactly as it does on the gameplay actor.
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (bobTarget != null)
        {
            bobBase = bobTarget.localPosition;
            hasBobTarget = true;
        }
    }

    private void OnEnable() => StartCoroutine(IdleRoutine());

    private void OnDisable()
    {
        // Rest at the authored position while hidden, so the actor never comes back mid-bob.
        if (hasBobTarget)
            bobTarget.localPosition = bobBase;
    }

    // One loop drives the bob and the frame swap together, so an actor that does both cannot
    // drift them apart, and every idle actor costs a single coroutine.
    private IEnumerator IdleRoutine()
    {
        int frameIndex = 0;
        float frameTime = Mathf.Max(0.01f, frameDuration);
        float nextFrame = Time.time + frameTime;

        while (true)
        {
            if (hasBobTarget)
            {
                float offset = Mathf.Sin(Time.time * bobSpeed) * bobAmount;
                bobTarget.localPosition = bobBase + new Vector3(0f, offset, 0f);
            }

            if (spriteRenderer != null && idleFrames != null && idleFrames.Length > 0 &&
                Time.time >= nextFrame)
            {
                frameIndex = (frameIndex + 1) % idleFrames.Length;
                spriteRenderer.sprite = idleFrames[frameIndex];
                nextFrame = Time.time + frameTime;
            }

            yield return null;
        }
    }
}
