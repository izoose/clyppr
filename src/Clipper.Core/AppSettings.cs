using System.Text.Json;

namespace Clipper.Core;

/// <summary>User settings, persisted as JSON under the app data folder.</summary>
public sealed class AppSettings
{
    public string ClipsDirectory { get; set; } = AppPaths.DefaultClipsDir;
    /// <summary>Process name (no ".exe") whose audio goes on its own "Voice" track and is cut from "Desktop".</summary>
    public string VoiceApp { get; set; } = "Discord";
    public bool MicEnabled { get; set; } = true;
    /// <summary>Specific microphone endpoint id, or null for the Windows default.</summary>
    public string? MicDeviceId { get; set; }
    /// <summary>Play a chime when a clip is saved.</summary>
    public bool ClipSoundEnabled { get; set; } = true;
    /// <summary>Clip chime volume, 0..1 (soft by default).</summary>
    public double ClipSoundVolume { get; set; } = 0.3;
    public int Fps { get; set; } = 60;
    /// <summary>NVENC constant-quality (lower = better/bigger). 19-23 typical.</summary>
    public int Cq { get; set; } = 21;
    /// <summary>How many seconds of replay buffer to keep in RAM/disk.</summary>
    public int BufferSeconds { get; set; } = 120;
    /// <summary>How much of the buffer a "clip" saves.</summary>
    public int ClipLengthSeconds { get; set; } = 30;
    public string HotkeyModifiers { get; set; } = "Alt";
    public string HotkeyKey { get; set; } = "C";
    public string ScreenshotModifiers { get; set; } = "Alt";
    public string ScreenshotKey { get; set; } = "S";
    public bool BufferEnabledOnStart { get; set; } = true;

    // Facecam
    public bool FacecamEnabled { get; set; }
    public string? FacecamDevice { get; set; }
    public int FacecamWidth { get; set; } = 320;
    public string FacecamCorner { get; set; } = "BottomRight";

    // Sharing (M5) — default to the Clyppr domain; add the token after deploying the server.
    public string? ShareEndpoint { get; set; } = "https://clyppr.com";
    public string? ShareToken { get; set; }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsPath)) ?? new();
        }
        catch { /* corrupt settings → defaults */ }
        return new();
    }

    public void Save()
    {
        try { File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(this, JsonOpts)); }
        catch { /* best effort */ }
    }
}
