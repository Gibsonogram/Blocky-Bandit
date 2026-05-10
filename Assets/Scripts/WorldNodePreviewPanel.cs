using TMPro;
using UnityEngine;

public class WorldNodePreviewPanel : MonoBehaviour
{
    [SerializeField] private TMP_Text worldNameText;
    [SerializeField] private TMP_Text collectableText;

    public void Refresh(WorldData data, int worldIndex)
    {
        gameObject.SetActive(true);
        worldNameText.text = data.worldName;
        int found = data.GetFoundCollectables(worldIndex);
        int total = data.GetTotalCollectables();
        collectableText.text = $"{found}/{total}";
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
