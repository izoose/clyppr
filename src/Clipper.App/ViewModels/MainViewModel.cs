using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Clipper.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Clipper.App;

public partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ClipLibrary _library;
    private readonly RecordingService _recording;

    public ObservableCollection<Clip> Clips { get; } = new();
    public ObservableCollection<AlbumViewModel> Albums { get; } = new();

    /// <summary>The clip whose "⋯" menu is currently open (set by the view).</summary>
    public Clip? ContextClip { get; set; }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _bufferRunning;
    [ObservableProperty] private bool _manualRecording;
    [ObservableProperty] private string _statusText = "Idle";
    [ObservableProperty] private string _toast = "";
    [ObservableProperty] private bool _showSettings;
    [ObservableProperty] private SettingsViewModel? _currentSettings;
    [ObservableProperty] private long? _selectedAlbumId;
    [ObservableProperty] private bool _allSelected = true;
    [ObservableProperty] private bool _favoritesOnly;

    public RecordingService Recording => _recording;

    public MainViewModel(AppSettings settings, ClipLibrary library, RecordingService recording)
    {
        _settings = settings;
        _library = library;
        _recording = recording;

        _recording.StateChanged += () => Dispatch(RefreshState);
        _recording.ClipSaved += clip => Dispatch(() => { InsertClip(clip); ShowToast($"Clipped “{clip.Title}”"); });
        _recording.Error += msg => Dispatch(() => ShowToast(msg));

        LoadAlbums();
        Reload();
        RefreshState();
    }

    partial void OnSearchTextChanged(string value) => Reload();

    private void Reload()
    {
        Clips.Clear();
        foreach (var c in _library.Query(SearchText, SelectedAlbumId, FavoritesOnly)) Clips.Add(c);
    }

    [RelayCommand]
    private void ToggleFavorite(Clip? clip)
    {
        if (clip is null) return;
        clip.IsFavorite = !clip.IsFavorite;
        _library.SetFavorite(clip.Id, clip.IsFavorite);
        Reload();   // refresh the star (and drop it if the Favorites filter is on and it was unfavorited)
    }

    [RelayCommand]
    private void ToggleFavoritesFilter()
    {
        FavoritesOnly = !FavoritesOnly;
        Reload();
    }

    private void LoadAlbums()
    {
        Albums.Clear();
        foreach (var a in _library.GetAlbums())
            Albums.Add(new AlbumViewModel(a) { IsSelected = a.Id == SelectedAlbumId });
        AllSelected = SelectedAlbumId is null;
    }

    // ---- album commands ----

    [RelayCommand]
    private void SelectAll()
    {
        SelectedAlbumId = null;
        foreach (var a in Albums) a.IsSelected = false;
        AllSelected = true;
        Reload();
    }

    [RelayCommand]
    private void SelectAlbum(AlbumViewModel? album)
    {
        if (album is null) return;
        SelectedAlbumId = album.Id;
        foreach (var a in Albums) a.IsSelected = a.Id == album.Id;
        AllSelected = false;
        Reload();
    }

    [RelayCommand]
    private void NewAlbum()
    {
        string? name = InputDialog.Ask(Application.Current.MainWindow, "New album name");
        if (name is null) return;
        var album = _library.AddAlbum(name);
        LoadAlbums();
        SelectAlbum(Albums.FirstOrDefault(a => a.Id == album.Id));
    }

    [RelayCommand]
    private void RenameAlbum(AlbumViewModel? album)
    {
        if (album is null) return;
        string? name = InputDialog.Ask(Application.Current.MainWindow, "Rename album", album.Name);
        if (name is null) return;
        _library.RenameAlbum(album.Id, name);
        album.Name = name;
    }

    [RelayCommand]
    private void DeleteAlbum(AlbumViewModel? album)
    {
        if (album is null) return;
        if (MessageBox.Show($"Delete album “{album.Name}”?\nClips stay, but are moved to All.",
                "Delete album", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _library.DeleteAlbum(album.Id);
        if (SelectedAlbumId == album.Id) SelectAll();
        LoadAlbums();
        Reload();
    }

    [RelayCommand]
    private void AddToAlbum(AlbumViewModel? album)
    {
        if (album is null || ContextClip is null) return;
        _library.SetClipAlbum(ContextClip.Id, album.Id);
        ContextClip.AlbumId = album.Id;
        if (SelectedAlbumId is not null && SelectedAlbumId != album.Id) Clips.Remove(ContextClip);
        ShowToast($"Added to “{album.Name}”");
    }

    [RelayCommand]
    private void RemoveFromAlbum()
    {
        if (ContextClip is null) return;
        _library.SetClipAlbum(ContextClip.Id, null);
        ContextClip.AlbumId = null;
        if (SelectedAlbumId is not null) Clips.Remove(ContextClip);
        ShowToast("Removed from album");
    }

    [RelayCommand]
    private void AddToNewAlbum()
    {
        if (ContextClip is null) return;
        string? name = InputDialog.Ask(Application.Current.MainWindow, "New album name");
        if (name is null) return;
        var album = _library.AddAlbum(name);
        _library.SetClipAlbum(ContextClip.Id, album.Id);
        ContextClip.AlbumId = album.Id;
        LoadAlbums();
        if (SelectedAlbumId is not null && SelectedAlbumId != album.Id) Clips.Remove(ContextClip);
        ShowToast($"Added to “{name}”");
    }

    private void InsertClip(Clip clip)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) Clips.Insert(0, clip);
        else Reload();
    }

    private void RefreshState()
    {
        BufferRunning = _recording.BufferRunning;
        ManualRecording = _recording.ManualRecording;
        StatusText = ManualRecording ? "Recording…" : BufferRunning ? "Buffering — press to clip" : "Idle";
    }

    // ---- commands ----

    [RelayCommand]
    private void ClipNow() => _recording.SaveClip();

    [RelayCommand]
    private void ToggleBuffer()
    {
        if (_recording.BufferRunning) _recording.StopBuffer();
        else _recording.StartBuffer();
    }

    [RelayCommand]
    private void ToggleRecord()
    {
        if (_recording.ManualRecording) _recording.StopManual();
        else _recording.StartManual();
    }

    [RelayCommand]
    private void Import()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import clips",
            Filter = "Videos|*.mp4;*.mkv;*.mov;*.webm;*.avi|All files|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var file in dlg.FileNames)
        {
            var clip = ClipImporter.Import(file, _library);
            Clips.Insert(0, clip);
        }
    }

    [RelayCommand]
    private void Play(Clip? clip)
    {
        if (clip is null || !File.Exists(clip.FilePath)) { ShowToast("File not found"); return; }
        try { Process.Start(new ProcessStartInfo(clip.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) { ShowToast(ex.Message); }
    }

    [RelayCommand]
    private void OpenFolder(Clip? clip)
    {
        if (clip is null || !File.Exists(clip.FilePath)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{clip.FilePath}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void CopyPath(Clip? clip)
    {
        if (clip is null) return;
        try { Clipboard.SetText(clip.FilePath); ShowToast("Path copied"); } catch { }
    }

    [RelayCommand]
    private void Edit(Clip? clip)
    {
        if (clip is null || !File.Exists(clip.FilePath)) { ShowToast("File not found"); return; }
        var evm = new EditorViewModel(clip, _settings, _library, exported => Dispatch(() =>
        {
            if (string.IsNullOrWhiteSpace(SearchText)) Clips.Insert(0, exported); else Reload();
            ShowToast($"Exported “{exported.Title}”");
        }));
        new EditorWindow(evm) { Owner = Application.Current.MainWindow }.Show();
    }

    [RelayCommand]
    private async Task Share(Clip? clip)
    {
        if (clip is null) return;
        if (!string.IsNullOrEmpty(clip.ShareUrl))
        {
            try { Clipboard.SetText(clip.ShareUrl); ShowToast("Link copied"); } catch { }
            return;
        }
        if (string.IsNullOrWhiteSpace(_settings.ShareEndpoint) || string.IsNullOrWhiteSpace(_settings.ShareToken))
        {
            ShowToast("Set up sharing in Settings first");
            return;
        }
        if (!File.Exists(clip.FilePath)) { ShowToast("File not found"); return; }

        ShowToast("Uploading…");
        try
        {
            string url = await ShareClient.UploadAsync(
                _settings.ShareEndpoint!, _settings.ShareToken!, clip.FilePath, clip.Title, clip.Width, clip.Height);
            clip.ShareUrl = url;
            _library.Update(clip);
            try { Clipboard.SetText(url); } catch { }
            ShowToast("Link copied — " + url);
        }
        catch (Exception ex) { ShowToast("Upload failed: " + ex.Message); }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        CurrentSettings = new SettingsViewModel(_settings, _recording, () =>
        {
            RefreshState();
            ShowSettings = false;
            ShowToast("Settings saved");
        });
        ShowSettings = true;
    }

    [RelayCommand]
    private void ShowLibrary() => ShowSettings = false;

    [RelayCommand]
    private void Delete(Clip? clip)
    {
        if (clip is null) return;
        var res = MessageBox.Show($"Delete “{clip.Title}”?\nThis removes the file from disk.",
            "Delete clip", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        _library.Delete(clip.Id);
        try { if (File.Exists(clip.FilePath)) File.Delete(clip.FilePath); } catch { }
        Clips.Remove(clip);
    }

    private void ShowToast(string msg)
    {
        Toast = msg;
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += (_, _) => { Toast = ""; t.Stop(); };
        t.Start();
    }

    private static void Dispatch(Action a) => Application.Current?.Dispatcher.Invoke(a);
}
