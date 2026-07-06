using System.IO;
using Clipper.Core;
using Clipper.Engine;

namespace Clipper.App;

/// <summary>
/// Owns the always-on replay buffer, the manual recorder, and the clip hotkey; on save it
/// imports the resulting MP4 into the library. Buffer and manual recording are mutually
/// exclusive (both need exclusive display capture).
/// </summary>
public sealed class RecordingService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly ClipLibrary _library;

    private ReplayBuffer? _buffer;
    private Recorder? _recorder;
    private GlobalHotkey? _hotkey;

    /// <summary>Raised (on a background thread) after a clip is saved and added to the library.</summary>
    public event Action<Clip>? ClipSaved;
    /// <summary>Raised when buffer/recording state changes so the UI can refresh.</summary>
    public event Action? StateChanged;
    public event Action<string>? Error;

    public bool BufferRunning => _buffer?.IsRunning ?? false;
    public bool ManualRecording => _recorder?.IsRecording ?? false;

    public RecordingService(AppSettings settings, ClipLibrary library)
    {
        _settings = settings;
        _library = library;
    }

    private RecorderConfig BuildConfig() => new()
    {
        OutputDirectory = _settings.ClipsDirectory,
        Fps = _settings.Fps,
        Cq = _settings.Cq,
        Tracks = RecorderConfig.DefaultTracks(_settings.VoiceApp),
    };

    // ---- replay buffer ----

    public void StartBuffer()
    {
        if (BufferRunning || ManualRecording) return;
        try
        {
            _buffer = new ReplayBuffer(BuildConfig(), _settings.BufferSeconds);
            _buffer.Start();
        }
        catch (Exception ex) { Error?.Invoke("Couldn't start capture: " + ex.Message); _buffer = null; }
        StateChanged?.Invoke();
    }

    public void StopBuffer()
    {
        _buffer?.Stop();
        _buffer?.Dispose();
        _buffer = null;
        StateChanged?.Invoke();
    }

    /// <summary>Save the last ClipLengthSeconds from the buffer and add it to the library.</summary>
    public Clip? SaveClip()
    {
        if (_buffer is null || !_buffer.IsRunning) return null;
        Directory.CreateDirectory(_settings.ClipsDirectory);
        string outPath = Path.Combine(_settings.ClipsDirectory, $"clip_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        try
        {
            string? saved = _buffer.SaveLast(_settings.ClipLengthSeconds, outPath);
            if (saved is null) return null;
            var clip = ClipImporter.Import(saved, _library, tracks: string.Join(",", _buffer.TrackNames));
            ClipSaved?.Invoke(clip);
            return clip;
        }
        catch (Exception ex) { Error?.Invoke("Save failed: " + ex.Message); return null; }
    }

    // ---- manual recording (Long Recording) ----

    public void StartManual()
    {
        if (ManualRecording) return;
        bool wasBuffer = BufferRunning;
        StopBuffer(); // release exclusive capture
        try
        {
            _recorder = new Recorder(BuildConfig());
            _recorder.Start();
        }
        catch (Exception ex)
        {
            Error?.Invoke("Couldn't start recording: " + ex.Message);
            _recorder = null;
            if (wasBuffer) StartBuffer();
        }
        StateChanged?.Invoke();
    }

    public Clip? StopManual()
    {
        if (_recorder is null) return null;
        Clip? clip = null;
        try
        {
            string file = _recorder.Stop();
            var names = _recorder.TrackNames;
            _recorder.Dispose();
            _recorder = null;
            if (File.Exists(file))
            {
                clip = ClipImporter.Import(file, _library, tracks: string.Join(",", names));
                ClipSaved?.Invoke(clip);
            }
        }
        catch (Exception ex) { Error?.Invoke("Stop failed: " + ex.Message); }
        if (_settings.BufferEnabledOnStart) StartBuffer();
        StateChanged?.Invoke();
        return clip;
    }

    // ---- hotkey ----

    public void RegisterHotkey()
    {
        _hotkey?.Dispose();
        var mods = ParseModifiers(_settings.HotkeyModifiers);
        uint vk = ParseKey(_settings.HotkeyKey);
        _hotkey = new GlobalHotkey(mods, vk);
        _hotkey.Pressed += () => SaveClip();
        _hotkey.Start();
    }

    public static HotkeyModifiers ParseModifiers(string s)
    {
        HotkeyModifiers m = HotkeyModifiers.None;
        foreach (var part in s.Split('+', ',', ' '))
        {
            switch (part.Trim().ToLowerInvariant())
            {
                case "alt": m |= HotkeyModifiers.Alt; break;
                case "control": case "ctrl": m |= HotkeyModifiers.Control; break;
                case "shift": m |= HotkeyModifiers.Shift; break;
                case "win": case "windows": m |= HotkeyModifiers.Win; break;
            }
        }
        return m == HotkeyModifiers.None ? HotkeyModifiers.Alt : m;
    }

    public static uint ParseKey(string key)
    {
        key = key.Trim().ToUpperInvariant();
        if (key.Length == 1)
        {
            char c = key[0];
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }
        if (key.StartsWith('F') && int.TryParse(key[1..], out int f) && f is >= 1 and <= 24)
            return (uint)(0x70 + (f - 1)); // VK_F1 = 0x70
        return 0x43; // default 'C'
    }

    /// <summary>Re-apply settings after the user changes them (hotkey + buffer restart).</summary>
    public void ApplySettings()
    {
        RegisterHotkey();
        if (BufferRunning) { StopBuffer(); StartBuffer(); }
    }

    public void Dispose()
    {
        _hotkey?.Dispose();
        _buffer?.Dispose();
        _recorder?.Dispose();
    }
}
