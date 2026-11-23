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

    // The danger overlay's player-facing options are display preferences rather than progress,
    // so they live here alongside the audio settings. They are static because the overlay lives
    // in every level scene: the in-game Visual settings screen and the overlay's custom
    // Inspector both read and write the same saved values. With no saved value they fall back to
    // VisionOverlayDefaults, i.e. the fields on the VisionOverlayStyle asset.
    //
    // The opacity key is versioned: it used to hold a multiplier on top of a separate authored
    // fill alpha (so 1 was the default), and it is now the overlay's own opacity. Reusing the
    // old key would turn a save written by those builds into a fully opaque overlay, so it
    // starts fresh and picks the current default up.
    private const string OverlayOpacityKey = "display.overlay.opacity.v2";
    private const string OverlayRadiusKey = "display.overlay.radius";

    // The frame-rate cap is a device preference like the audio and overlay settings, so it
    // lives here too. The pref holds the target fps itself (30, 60, or -1 for uncapped)
    // rather than an enum index, so adding or reordering presets later cannot reinterpret
    // an existing save.
    private const string FrameRateKey = "display.frameRate";

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

    // Defaults to 60 fps for players who never opened the options screen. An unrecognised
    // stored value (corrupted/hand-edited pref) also falls back to that default, so the
    // game can never be left uncapped by accident.
    public static FrameRatePreset FrameRate
    {
        get
        {
            switch (PlayerPrefs.GetInt(FrameRateKey, 60))
            {
                case 30: return FrameRatePreset.Fps30;
                case 60: return FrameRatePreset.Fps60;
                case -1: return FrameRatePreset.Uncapped;
                default: return FrameRatePreset.Fps60;
            }
        }
        set
        {
            PlayerPrefs.SetInt(FrameRateKey, FrameRateSettings.TargetFps(value));
            PlayerPrefs.Save();
        }
    }

    public static float OverlayOpacity
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(OverlayOpacityKey, VisionOverlayDefaults.Opacity));
        set { PlayerPrefs.SetFloat(OverlayOpacityKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }

    public static float OverlayRadius
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(OverlayRadiusKey, VisionOverlayDefaults.Radius),
                           VisionOverlayDefaults.MinRadius, VisionOverlayDefaults.MaxRadius);
        set
        {
            PlayerPrefs.SetFloat(OverlayRadiusKey,
                Mathf.Clamp(value, VisionOverlayDefaults.MinRadius, VisionOverlayDefaults.MaxRadius));
            PlayerPrefs.Save();
        }
    }

    // Restores both overlay preferences to VisionOverlayDefaults. The caller re-applies them to
    // the live overlay; nothing here touches the renderer.
    public static void ResetOverlayDefaults()
    {
        OverlayOpacity = VisionOverlayDefaults.Opacity;
        OverlayRadius = VisionOverlayDefaults.Radius;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Load();

        // Apply the saved cap before the first frame so a relaunch never renders even one
        // frame at the quality level's own vsync setting (Ultra ships with vsync on).
        FrameRateSettings.Apply(FrameRate);
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
