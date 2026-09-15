using UnityEngine;

public class AtmosphereManager : MonoBehaviour
{
    public static AtmosphereManager Instance {get; private set; }

    private GameObject currentInstance;
    
    void Awake()
    {
        Instance = this;
    }

    public void SetAtmosphere(GameObject prefab)
    {
        ParticleSystem staleParticleSystem = GetComponentInChildren<ParticleSystem>();
        if (staleParticleSystem != null)
            Destroy(staleParticleSystem);

        currentInstance = prefab != null ? Instantiate(prefab, this.transform) : null;
    } 
    

}
