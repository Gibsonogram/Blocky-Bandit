using System.Collections;
using UnityEngine;

public class Collectable : MonoBehaviour, IGridActor
{
    [SerializeField] private float animDuration = 0.25f;


    public bool OnPlayerMoveInto(Vector2Int direction)
    {
        // A bunch of housekeeping. set rigidbody to false, play short anim, 
        // increment manager counter, destroy...
        GetComponent<Collider2D>().enabled = false;
        CollectableManager.Instance.RegisterCollection();
        StartCoroutine(TriggerCollection());
        return true;
    }

    IEnumerator TriggerCollection()
    {
        // lerp upward on collect
        Vector3 startPos = transform.position;
        Vector3 targetPos = startPos + new Vector3(0f, 0.75f, 0f);
        float elapsed = 0f;
        while (elapsed < animDuration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(startPos, targetPos, elapsed / animDuration);
            yield return null;
        }

        Destroy(gameObject);
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        CollectableManager.Instance.RegisterTotal();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
