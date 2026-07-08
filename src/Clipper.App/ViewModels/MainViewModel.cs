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
    public ObservableCollection<GameChipViewModel> Games { get; } = new();

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
    [ObservableProperty] private string _nowGame = "Ready";
    [ObservableProperty] private int _clipMinutes = 1;
    [ObservableProperty] private bool _selectionMode;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string? _selectedGame;
    [ObservableProperty] private string? _selectedPlayer;

    public int ClipCount => Clips.Count;
    public string HotkeyKeyDisplay => _settings.HotkeyKey;

    public RecordingService Recording => _recording;

    public MainViewModel(AppSettings settings, ClipLibrary library, RecordingService recording)
    {
        _settings = settings;
        _library = library;
        _recording = recording;

        _recording.StateChanged += () => Dispatch(RefreshState);
        _recording.ClipSaved += clip => Dispatch(() =>
        {
            if (_settings.ClipSoundEnabled) Sounds.PlayClip((float)_settings.ClipSoundVolume);
            InsertClip(clip);
            ShowToast($"Clipped “{clip.Title}”");
        });
        _recording.Error += msg => Dispatch(() => ShowToast(msg));

        ClipMinutes = Math.Max(1, settings.ClipLengthSeconds / 60);
        Clips.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ClipCount));

        var gameTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        gameTimer.Tick += (_, _) => NowGame = AppDetector.ForegroundGame() ?? "Ready";
        gameTimer.Start();

        LoadGames();
        LoadAlbums();
        Reload();
        RefreshState();
    }

    partial void OnSearchTextChanged(string value) => Reload();

    private void Reload()
    {
        Clips.Clear();
        foreach (var c in _library.Query(SearchText, SelectedAlbumId, FavoritesOnly, SelectedGame, SelectedPlayer)) Clips.Add(c);
    }

    /// <summary>Filter the library to clips a detected player appears in (from the viewer's chips).</summary>
    public void FilterByPlayer(string name)
    {
        SelectedPlayer = name;
        SelectedGame = null;
        SelectedAlbumId = null;
        FavoritesOnly = false;
        AllSelected = false;
        foreach (var a in Albums) a.IsSelected = false;
        foreach (var g in Games) g.IsSelected = false;
        ShowSettings = false;
        Reload();
    }

    [RelayCommand]
    private void ClearPlayerFilter()
    {
        SelectedPlayer = null;
        SelectAll();
    }

    private void LoadGames()
    {
        Games.Clear();
        foreach (var (name, count) in _library.GetGames())
            Games.Add(new GameChipViewModel(name, count) { IsSelected = string.Equals(name, SelectedGame, StringComparison.OrdinalIgnoreCase) });
    }

    [RelayCommand]
    private void SelectGame(GameChipViewModel? game)
    {
        if (game is null) return;
        SelectedGame = game.Name;
        SelectedPlayer = null;
        SelectedAlbumId = null;
        FavoritesOnly = false;
        AllSelected = false;
        foreach (var a in Albums) a.IsSelected = false;
        foreach (var g in Games) g.IsSelected = ReferenceEquals(g, game);
        Reload();
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
        SelectedGame = null;
        SelectedPlayer = null;
        SelectedAlbumId = null;
        AllSelected = false;
        foreach (var a in Albums) a.IsSelected = false;
        foreach (var g in Games) g.IsSelected = false;
        Reload();
    }

    private void LoadAlbums()
    {
        Albums.Clear();
        var gameNames = new HashSet<string>(Games.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var a in _library.GetAlbums())
        {
            if (gameNames.Contains(a.Name)) continue;   // a same-named game chip already covers this
            Albums.Add(new AlbumViewModel(a) { IsSelected = a.Id == SelectedAlbumId });
        }
        AllSelected = SelectedAlbumId is null;
    }

    // ---- album commands ----

    [RelayCommand]
    private void SelectAll()
    {
        SelectedAlbumId = null;
        SelectedGame = null;
        SelectedPlayer = null;
        foreach (var a in Albums) a.IsSelected = false;
        foreach (var g in Games) g.IsSelected = false;
        AllSelected = true;
        Reload();
    }

    [RelayCommand]
    private void SelectAlbum(AlbumViewModel? album)
    {
        if (album is null) return;
        SelectedAlbumId = album.Id;
        SelectedGame = null;
        SelectedPlayer = null;
        foreach (var a in Albums) a.IsSelected = a.Id == album.Id;
        foreach (var g in Games) g.IsSelected = false;
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
        LoadGames();   // a new game may have appeared
        LoadAlbums();  // keep game/album dedupe in sync
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

    /// <summary>Re-query the library so card visuals reflect external changes (favorite/title/etc.).</summary>
    public void RefreshCards() { LoadGames(); LoadAlbums(); Reload(); }

    [RelayCommand]
    private void OpenViewer(Clip? clip)
    {
        if (clip is null) return;
        int idx = Clips.IndexOf(clip);
        if (idx < 0) return;
        var vvm = new ClipViewerViewModel(this, _library, idx);
        new ClipViewerWindow(vvm) { Owner = Application.Current.MainWindow }.Show();
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
    private void Share(Clip? clip)
    {
        if (clip is null) return;
        if (!File.Exists(clip.FilePath)) { ShowToast("File not found"); return; }
        new ShareWindow(clip) { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    [RelayCommand]
    private void OpenMontage()
    {
        var mvm = new MontageViewModel(_settings, _library, exported => Dispatch(() =>
        {
            if (string.IsNullOrWhiteSpace(SearchText) && SelectedAlbumId is null && !FavoritesOnly) Clips.Insert(0, exported);
            else Reload();
            ShowToast($"Exported “{exported.Title}”");
        }));
        new MontageWindow(mvm) { Owner = Application.Current.MainWindow }.Show();
    }

    [RelayCommand]
    private void EditTags(Clip? clip)
    {
        if (clip is null) return;
        string? tags = InputDialog.Ask(Application.Current.MainWindow, "Hashtags (comma-separated)", clip.Tags);
        if (tags is null) return;
        clip.Tags = tags;
        _library.Update(clip);
        ShowToast("Hashtags updated");
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
    private void ShowLibrary()
    {
        ShowSettings = false;
        FavoritesOnly = false;
        SelectAll();   // clear album/favorites filters and show everything
    }

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

    // ---- multi-select ----
    public void RefreshSelectionCount() => SelectedCount = Clips.Count(c => c.IsSelected);

    [RelayCommand]
    private void ToggleSelection()
    {
        SelectionMode = !SelectionMode;
        if (!SelectionMode) ClearSelection();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var c in Clips) c.IsSelected = false;
        RefreshSelectionCount();
    }

    [RelayCommand]
    private void SelectAllClips()
    {
        bool all = Clips.All(c => c.IsSelected);
        foreach (var c in Clips) c.IsSelected = !all;   // toggle: select all, or clear if already all
        RefreshSelectionCount();
    }

    [RelayCommand]
    private void CopySelectedFiles()
    {
        var files = new System.Collections.Specialized.StringCollection();
        foreach (var c in Clips.Where(c => c.IsSelected && File.Exists(c.FilePath))) files.Add(c.FilePath);
        if (files.Count == 0) { ShowToast("Nothing selected"); return; }
        try { Clipboard.SetFileDropList(files); ShowToast($"Copied {files.Count} file{(files.Count == 1 ? "" : "s")} — paste anywhere"); } catch { }
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        var chosen = Clips.Where(c => c.IsSelected).ToList();
        if (chosen.Count == 0) return;
        var res = MessageBox.Show($"Delete {chosen.Count} clip{(chosen.Count == 1 ? "" : "s")}?\nThis removes the files from disk.",
            "Delete clips", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        foreach (var clip in chosen)
        {
            _library.Delete(clip.Id);
            try { if (File.Exists(clip.FilePath)) File.Delete(clip.FilePath); } catch { }
            Clips.Remove(clip);
        }
        SelectionMode = false;
        RefreshSelectionCount();
        ShowToast($"Deleted {chosen.Count} clip{(chosen.Count == 1 ? "" : "s")}");
    }

    [RelayCommand]
    private void AddSelectedToAlbum(AlbumViewModel? album)
    {
        if (album is null) return;
        var chosen = Clips.Where(c => c.IsSelected).ToList();
        if (chosen.Count == 0) return;
        foreach (var clip in chosen) _library.SetClipAlbum(clip.Id, album.Id);
        if (SelectedAlbumId is not null && SelectedAlbumId != album.Id)
            foreach (var clip in chosen) Clips.Remove(clip);
        SelectionMode = false;
        RefreshSelectionCount();
        ShowToast($"Added {chosen.Count} to “{album.Name}”");
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
