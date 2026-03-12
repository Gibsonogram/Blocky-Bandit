using UnityEngine;
using System.Collections;

public class Crate : MonoBehaviour
{
    
    
    private static readonly int PushTrigger = Animator.StringToHash("Push");
    private static readonly int BumpTrigger = Animator.StringToHash("Bump");
    private static readonly float TileSize = 1f;
    
    private Animator animator;
    private bool isMoving;

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }
    
    
    // We try to push the block
    

    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
