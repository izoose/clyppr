using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Clipper.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Clipper.App;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly RecordingService _recording;
    private readonly Action _onSaved;

    [ObservableProperty] private string _clipsDirectory;
    [ObservableProperty] private string _voiceApp;
    [ObservableProperty] private int _fps;
    [ObservableProperty] private int _cq;
    [ObservableProperty] private int _bufferSeconds;
    [ObservableProperty] private int _clipLengthSeconds;
    [ObservableProperty] private string _hotkeyModifiers;
    [ObservableProperty] private string _hotkeyKey;
    [ObservableProperty] private string _screenshotModifiers;
    [ObservableProperty] private string _screenshotKey;
    [ObservableProperty] private bool _bufferEnabledOnStart;
    [ObservableProperty] private string _shareEndpoint;
    [ObservableProperty] private string _shareToken;
    [ObservableProperty] private bool _runOnStartup;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _section = "Recording";
    [ObservableProperty] private bool _facecamEnabled;
    [ObservableProperty] private string? _facecamDevice;
    [ObservableProperty] private int _facecamWidth;
    [ObservableProperty] private string _facecamCorner;

    public ObservableCollection<string> Cameras { get; } = new();
    public string[] Corners { get; } = { "BottomRight", "BottomLeft", "TopRight", "TopLeft" };

    public SettingsViewModel(AppSettings settings, RecordingService recording, Action onSaved)
    {
        _settings = settings;
        _recording = recording;
        _onSaved = onSaved;

        _clipsDirectory = settings.ClipsDirectory;
        _voiceApp = settings.VoiceApp;
        _fps = settings.Fps;
        _cq = settings.Cq;
        _bufferSeconds = settings.BufferSeconds;
        _clipLengthSeconds = settings.ClipLengthSeconds;
        _hotkeyModifiers = settings.HotkeyModifiers;
        _hotkeyKey = settings.HotkeyKey;
        _screenshotModifiers = settings.ScreenshotModifiers;
        _screenshotKey = settings.ScreenshotKey;
        _bufferEnabledOnStart = settings.BufferEnabledOnStart;
        _shareEndpoint = settings.ShareEndpoint ?? "";
        _shareToken = settings.ShareToken ?? "";
        _runOnStartup = StartupManager.IsEnabled();
        _facecamEnabled = settings.FacecamEnabled;
        _facecamDevice = settings.FacecamDevice;
        _facecamWidth = settings.FacecamWidth;
        _facecamCorner = settings.FacecamCorner;

        // Enumerate webcams off the UI thread.
        Task.Run(() =>
        {
            var cams = CameraDevices.List();
            Application.Current?.Dispatcher.Invoke(() => { foreach (var c in cams) Cameras.Add(c); });
        });
    }

    [RelayCommand]
    private void SelectSection(string section) => Section = section;

    [RelayCommand]
    private void BrowseClipsDir()
    {
        var dlg = new OpenFolderDialog { Title = "Choose clips folder", InitialDirectory = ClipsDirectory };
        if (dlg.ShowDialog() == true) ClipsDirectory = dlg.FolderName;
    }

    [RelayCommand]
    private void Save()
    {
        _settings.ClipsDirectory = ClipsDirectory;
        _settings.VoiceApp = VoiceApp;
        _settings.Fps = Math.Clamp(Fps, 24, 240);
        _settings.Cq = Math.Clamp(Cq, 10, 40);
        _settings.BufferSeconds = Math.Clamp(BufferSeconds, 15, 1200);
        _settings.ClipLengthSeconds = Math.Clamp(ClipLengthSeconds, 5, BufferSeconds);
        _settings.HotkeyModifiers = string.IsNullOrWhiteSpace(HotkeyModifiers) ? "Alt" : HotkeyModifiers;
        _settings.HotkeyKey = string.IsNullOrWhiteSpace(HotkeyKey) ? "C" : HotkeyKey;
        _settings.ScreenshotModifiers = string.IsNullOrWhiteSpace(ScreenshotModifiers) ? "Alt" : ScreenshotModifiers;
        _settings.ScreenshotKey = string.IsNullOrWhiteSpace(ScreenshotKey) ? "S" : ScreenshotKey;
        _settings.BufferEnabledOnStart = BufferEnabledOnStart;
        _settings.ShareEndpoint = string.IsNullOrWhiteSpace(ShareEndpoint) ? null : ShareEndpoint.Trim();
        _settings.ShareToken = string.IsNullOrWhiteSpace(ShareToken) ? null : ShareToken.Trim();
        _settings.FacecamEnabled = FacecamEnabled;
        _settings.FacecamDevice = FacecamDevice;
        _settings.FacecamWidth = Math.Clamp(FacecamWidth, 120, 640);
        _settings.FacecamCorner = FacecamCorner;
        _settings.Save();

        StartupManager.Set(RunOnStartup, Process.GetCurrentProcess().MainModule?.FileName ?? "");
        _recording.ApplySettings();

        _onSaved();
        Status = "Saved ✓";
    }
}
