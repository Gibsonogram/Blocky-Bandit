using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    private const string SaveFileName = "save.json";

    // Audio preferences are device settings rather than progress, so they live in
    // PlayerPrefs: GetInt can supply a default for players who never touched them,
    // whereas a new bool field on SaveData would deserialize as false for existing saves.
    private const string SfxEnabledKey = "audio.sfx.enabled";
    private const string MusicEnabledKey = "audio.music.enabled";

    private SaveData saveData;

    public bool SfxEnabled
    {
        get => PlayerPrefs.GetInt(SfxEnabledKey, 1) == 1;
        set { PlayerPrefs.SetInt(SfxEnabledKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public bool MusicEnabled
    {
        get => PlayerPrefs.GetInt(MusicEnabledKey, 1) == 1;
        set { PlayerPrefs.SetInt(MusicEnabledKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

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

    // Returns the sum of bestCollectables across all saved level records
    public int GetTotalCollectables()
    {
        int total = 0;
        foreach (LevelRecord record in saveData.levelRecords)
            total += record.bestCollectables;
        return total;
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
