using UnityEngine;

public class CollectableManager : MonoBehaviour
{
    public static CollectableManager Instance { get; private set; }
    [SerializeField] public int totalCollectables = 0;
    [SerializeField] public int foundCollectables = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void RegisterTotal()
    {
        // collectibles call in with this on their awake, 
        // this makes the total match however many there are in the scene...
        totalCollectables += 1;
    }

    public void RegisterCollection()
    {
        foundCollectables += 1;
    }

    public void ResetCount()
    {
        // TODO:
        // create a separate function to add collectable count to save data. 
        // If cur count > count currently saved, then save, else don't.
        SaveCollectableData();
        totalCollectables = 0;
        foundCollectables = 0;
    }

    void SaveCollectableData()
    {
        return;
    } 

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}