using UnityEngine;

public class AtmosphereManager : MonoBehaviour
{
    public static AtmosphereManager Instance {get; private set; }

    void Awake()
    {
        Instance = this;
    }

    public void SetAtmosphere(GameObject prefab)
    {
        ParticleSystem staleParticleSystem = GetComponentInChildren<ParticleSystem>();
        if (staleParticleSystem != null)
            Destroy(staleParticleSystem);

        Instantiate(prefab, this.transform);
    } 
    

}
