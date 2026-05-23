using UnityEngine;

[CreateAssetMenu(menuName = "Game/World Data")]
public class WorldData : ScriptableObject
{
    public string worldName;
    public string[] levelSceneNames;

}
