using System.Collections;
using UnityEngine;

public class WorldMapCursor : MonoBehaviour
{
    public static WorldMapCursor Instance { get; private set; }

    [SerializeField] private Transform cursorIcon;
    [SerializeField] private float lerpDuration = 0.3f;

    private bool isMoving;
    private Coroutine lerpCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        GameStateManager.Instance.OnStateChanged += OnStateChanged;
        cursorIcon.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        GameStateManager.Instance.OnStateChanged -= OnStateChanged;
    }

    private void OnStateChanged(GameState newState)
    {
        cursorIcon.gameObject.SetActive(newState == GameState.WorldMap);
    }

    public void SnapToNode(WorldMapNode node)
    {
        if (lerpCoroutine != null) StopCoroutine(lerpCoroutine);
        isMoving = false;
        cursorIcon.position = node.transform.position;
    }

    public void MoveToNode(WorldMapNode node)
    {
        if (lerpCoroutine != null) StopCoroutine(lerpCoroutine);
        lerpCoroutine = StartCoroutine(LerpToNode(node.transform.position));
    }

    private IEnumerator LerpToNode(Vector3 target)
    {
        isMoving = true;
        float elapsed = 0f;
        Vector3 start = cursorIcon.position;
        while (elapsed < lerpDuration)
        {
            cursorIcon.position = Vector3.Lerp(start, target, elapsed / lerpDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        cursorIcon.position = target;
        isMoving = false;
    }
}
