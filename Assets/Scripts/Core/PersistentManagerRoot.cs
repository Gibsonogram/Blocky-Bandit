using UnityEngine;

public class PersistentManagerRoot : MonoBehaviour
{
    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }
}
