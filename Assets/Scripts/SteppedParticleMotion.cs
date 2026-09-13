using UnityEngine;

public class SteppedParticleMotion : MonoBehaviour
{
    [SerializeField] public float stepsPerSecond = 6f;

    private ParticleSystem targetSystem;
    private float accumulatedTime;

    void Awake()
    {
        targetSystem = GetComponentInChildren<ParticleSystem>();
        targetSystem.Pause(true);
    }

    // Update is called once per frame
    void Update()
    {
        
        accumulatedTime += Time.deltaTime;
        float stepInterval = 1f / stepsPerSecond;
        if (accumulatedTime >= stepInterval)
        {
            targetSystem.Simulate(accumulatedTime, true, false, true);
            accumulatedTime = 0f;
        }
    }
}
