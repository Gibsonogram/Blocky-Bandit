using UnityEngine;

// The player-facing frame-rate presets. SaveManager owns where the choice is stored;
// this class owns what each preset means to Unity, so the options screen never touches
// the graphics API directly.
public enum FrameRatePreset
{
    Fps30,
    Fps60,
    Uncapped
}

public static class FrameRateSettings
{
    // The order the options-screen button cycles through.
    public static FrameRatePreset Next(FrameRatePreset current)
    {
        switch (current)
        {
            case FrameRatePreset.Fps30: return FrameRatePreset.Fps60;
            case FrameRatePreset.Fps60: return FrameRatePreset.Uncapped;
            default: return FrameRatePreset.Fps30;
        }
    }

    public static string Label(FrameRatePreset preset)
    {
        switch (preset)
        {
            case FrameRatePreset.Fps30: return "30 fps";
            case FrameRatePreset.Fps60: return "60 fps";
            default: return "Uncapped";
        }
    }

    // The value handed to Application.targetFrameRate: -1 means "no target", i.e. render as
    // fast as the machine can.
    public static int TargetFps(FrameRatePreset preset)
    {
        switch (preset)
        {
            case FrameRatePreset.Fps30: return 30;
            case FrameRatePreset.Fps60: return 60;
            default: return -1;
        }
    }

    // Desktop/Web: a vSyncCount above zero makes Unity ignore targetFrameRate, so every
    // preset — including Uncapped — turns vsync off and lets targetFrameRate do the pacing.
    // On Android/iOS vsync is ignored anyway and targetFrameRate = -1 means the platform
    // default (30 fps), which is the closest those platforms have to the same intent.
    public static void Apply(FrameRatePreset preset)
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFps(preset);
    }
}
