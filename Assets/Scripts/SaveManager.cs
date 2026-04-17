using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    private const string SaveFileName = "save.json";
    private SaveData saveData;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Load();
    }

    // Returns the best collectable count for a given level, 0 if never played
    public int GetBestCollectables(int worldIndex, int levelIndex)
    {
        return GetRecord(worldIndex, levelIndex)?.bestCollectables ?? 0;
    }

    // Saves count only if it beats the existing record
    public void SaveCollectables(int worldIndex, int levelIndex, int count)
    {
        LevelRecord record = GetRecord(worldIndex, levelIndex);
        if (record == null)
        {
            saveData.levelRecords.Add(new LevelRecord
            {
                worldIndex = worldIndex,
                levelIndex = levelIndex,
                bestCollectables = count
            });
            Save();
            return;
        }

        if (count > record.bestCollectables)
        {
            record.bestCollectables = count;
            Save();
        }
    }

    private LevelRecord GetRecord(int worldIndex, int levelIndex)
    {
        return saveData.levelRecords.Find(r => r.worldIndex == worldIndex && r.levelIndex == levelIndex);
    }

    private void Save()
    {
        File.WriteAllText(SavePath, JsonUtility.ToJson(saveData, prettyPrint: true));
    }

    private void Load()
    {
        saveData = File.Exists(SavePath)
            ? JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath))
            : new SaveData();
    }

    private string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);
}

[System.Serializable]
public class SaveData
{
    public List<LevelRecord> levelRecords = new List<LevelRecord>();
}

[System.Serializable]
public class LevelRecord
{
    public int worldIndex;
    public int levelIndex;
    public int bestCollectables;
}
