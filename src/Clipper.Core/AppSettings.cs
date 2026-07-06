using System.Text.Json;

namespace Clipper.Core;

/// <summary>User settings, persisted as JSON under the app data folder.</summary>
public sealed class AppSettings
{
    public string ClipsDirectory { get; set; } = AppPaths.DefaultClipsDir;
    /// <summary>Process name (no ".exe") whose audio goes on its own "Voice" track and is cut from "Desktop".</summary>
    public string VoiceApp { get; set; } = "Discord";
    public int Fps { get; set; } = 60;
    /// <summary>NVENC constant-quality (lower = better/bigger). 19-23 typical.</summary>
    public int Cq { get; set; } = 21;
    /// <summary>How many seconds of replay buffer to keep in RAM/disk.</summary>
    public int BufferSeconds { get; set; } = 120;
    /// <summary>How much of the buffer a "clip" saves.</summary>
    public int ClipLengthSeconds { get; set; } = 30;
    public string HotkeyModifiers { get; set; } = "Alt";
    public string HotkeyKey { get; set; } = "C";
    public bool BufferEnabledOnStart { get; set; } = true;

    // Sharing (M5)
    public string? ShareEndpoint { get; set; }
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
