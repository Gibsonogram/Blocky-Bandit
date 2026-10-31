using System;
using UnityEngine;

// Owns the game's music and one-shot SFX playback and keeps both in sync with the
// player's saved audio preferences. No clips ship with the project yet, so the menu
// toggles currently only gate this manager's sources; anything added later should play
// through PlayMusic/PlaySfx so it respects the same settings.
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource sfxSource;

    public bool MusicEnabled { get; private set; } = true;
    public bool SfxEnabled { get; private set; } = true;

    // Raised whenever either setting changes, so open UI can refresh its labels.
    public event Action SettingsChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (musicSource != null)
        {
            musicSource.loop = true;
            musicSource.playOnAwake = false;
        }

        if (sfxSource != null)
            sfxSource.playOnAwake = false;
    }

    private void Start()
    {
        // SaveManager is a sibling in the persistent managers root and loads its
        // preferences in Awake, so reading them here always sees the stored values.
        if (SaveManager.Instance != null)
        {
            MusicEnabled = SaveManager.Instance.MusicEnabled;
            SfxEnabled = SaveManager.Instance.SfxEnabled;
        }

        Apply();
    }

    public void ToggleMusic() => SetMusicEnabled(!MusicEnabled);
    public void ToggleSfx() => SetSfxEnabled(!SfxEnabled);

    public void SetMusicEnabled(bool enabled)
    {
        if (MusicEnabled == enabled) return;

        MusicEnabled = enabled;
        if (SaveManager.Instance != null) SaveManager.Instance.MusicEnabled = enabled;

        Apply();
        SettingsChanged?.Invoke();
    }

    public void SetSfxEnabled(bool enabled)
    {
        if (SfxEnabled == enabled) return;

        SfxEnabled = enabled;
        if (SaveManager.Instance != null) SaveManager.Instance.SfxEnabled = enabled;

        Apply();
        SettingsChanged?.Invoke();
    }

    // Plays a one-shot effect unless the player has sound effects turned off.
    public void PlaySfx(AudioClip clip, float volume = 1f)
    {
        if (!SfxEnabled || clip == null || sfxSource == null) return;

        sfxSource.PlayOneShot(clip, volume);
    }

    // Starts (or swaps) the looping background track, keeping it silent while music is off.
    public void PlayMusic(AudioClip clip)
    {
        if (musicSource == null || clip == null) return;

        musicSource.clip = clip;
        musicSource.loop = true;

        if (MusicEnabled) musicSource.Play();
    }

    public void StopMusic()
    {
        if (musicSource != null) musicSource.Stop();
    }

    private void Apply()
    {
        if (musicSource != null)
        {
            musicSource.mute = !MusicEnabled;
            if (MusicEnabled && !musicSource.isPlaying && musicSource.clip != null)
                musicSource.Play();
        }

        if (sfxSource != null && !SfxEnabled)
            sfxSource.Stop();
    }
}
