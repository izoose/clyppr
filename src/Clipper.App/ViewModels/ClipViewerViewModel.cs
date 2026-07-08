using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using Clipper.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipper.App;

/// <summary>A detected player and the timestamp (seconds) where they first appear in the clip.</summary>
public sealed record PlayerHit(string Name, double Time);

/// <summary>Medal-style clip viewer: video + prev/next + editable details (title, favorite,
/// album, hashtags), Open in Editor / Share / Delete.</summary>
public partial class ClipViewerViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly ClipLibrary _library;

    public ObservableCollection<Clip> Clips => _main.Clips;
    public MainViewModel Main => _main;

    [ObservableProperty] private int _index;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _playerStatus = "";

    /// <summary>Roblox usernames detected in the clip (OCR of overhead nametags), with the time seen.</summary>
    public ObservableCollection<PlayerHit> Players { get; } = new();

    /// <summary>Raised when a detected player is clicked — the view seeks the video there.</summary>
    public event Action<double>? SeekRequested;

    [RelayCommand]
    private void SeekToPlayer(PlayerHit? p) { if (p is not null) SeekRequested?.Invoke(p.Time); }

    /// <summary>Raised when the view should reload the MediaElement (clip changed).</summary>
    public event Action? SourceChanged;
    /// <summary>Raised when the viewer should close (last clip deleted).</summary>
    public event Action? RequestClose;

    public ClipViewerViewModel(MainViewModel main, ClipLibrary library, int index)
    {
        _main = main;
        _library = library;
        _index = Math.Clamp(index, 0, Math.Max(0, Clips.Count - 1));
        _title = Current?.Title ?? "";
    }

    public Clip? Current => Index >= 0 && Index < Clips.Count ? Clips[Index] : null;
    public string FilePath => Current?.FilePath ?? "";
    public string DateText => Current?.CreatedAt.ToString("MMMM d, yyyy") ?? "";
    public string SizeText => Current?.SizeText ?? "";
    public string DurationText => Current?.DurationText ?? "";
    public string GameText => string.IsNullOrWhiteSpace(Current?.Game) ? "Unknown" : Current!.Game!;
    public string TagsText => string.IsNullOrWhiteSpace(Current?.Tags) ? "Add hashtags…" : Current!.Tags;
    public bool IsFavorite => Current?.IsFavorite ?? false;
    public bool CanPrev => Index > 0;
    public bool CanNext => Index < Clips.Count - 1;

    partial void OnIndexChanged(int value)
    {
        Title = Current?.Title ?? "";
        NotifyAll();
        SourceChanged?.Invoke();
        ScanPlayers();
    }

    /// <summary>Populate the Players list — from cache if already scanned, else OCR the clip in the
    /// background. Only Roblox clips are scanned (that's where @username nametags appear).</summary>
    public async void ScanPlayers()
    {
        Players.Clear();
        PlayerStatus = "";
        var clip = Current;
        if (clip is null) return;

        bool isRoblox = (clip.Game ?? "").Contains("roblox", StringComparison.OrdinalIgnoreCase);
        if (!isRoblox && clip.Players is null) return;   // don't OCR non-Roblox clips

        if (clip.Players is not null) { LoadPlayersFrom(clip.Players); return; }   // cached

        PlayerStatus = "Scanning for players…";
        var target = clip;
        List<(string Name, double Time)> hits;
        try { hits = await PlayerScanner.ScanAsync(target.FilePath); }
        catch { hits = new(); }

        target.Players = string.Join(",", hits.Select(h => $"{h.Name}:{h.Time.ToString(CultureInfo.InvariantCulture)}"));
        try { _library.SetPlayers(target.Id, target.Players); } catch { }

        if (!ReferenceEquals(Current, target)) return;   // navigated away mid-scan
        LoadPlayersFrom(target.Players);
    }

    private void LoadPlayersFrom(string csv)
    {
        Players.Clear();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int idx = part.LastIndexOf(':');
            string name = idx > 0 ? part[..idx] : part;
            double time = 0;
            if (idx > 0) double.TryParse(part[(idx + 1)..], NumberStyles.Any, CultureInfo.InvariantCulture, out time);
            Players.Add(new PlayerHit(name, time));
        }
        PlayerStatus = Players.Count == 0 ? "No players detected" : "";
    }

    partial void OnTitleChanged(string value)
    {
        if (Current is { } c && c.Title != value) { c.Title = value; _library.Update(c); }
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(GameText));
        OnPropertyChanged(nameof(TagsText));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(CanPrev));
        OnPropertyChanged(nameof(CanNext));
    }

    [RelayCommand] private void Next() { if (CanNext) Index++; }
    [RelayCommand] private void Prev() { if (CanPrev) Index--; }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (Current is not { } c) return;
        c.IsFavorite = !c.IsFavorite;
        _library.SetFavorite(c.Id, c.IsFavorite);
        OnPropertyChanged(nameof(IsFavorite));
    }

    [RelayCommand] private void OpenInEditor() { if (Current is { } c) _main.EditCommand.Execute(c); }
    [RelayCommand] private void Share() { if (Current is { } c) _main.ShareCommand.Execute(c); }

    [RelayCommand]
    private void OpenFolder()
    {
        if (Current is { } c && File.Exists(c.FilePath))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{c.FilePath}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void EditTags()
    {
        if (Current is not { } c) return;
        string? t = InputDialog.Ask(Application.Current.MainWindow, "Hashtags (comma-separated)", c.Tags);
        if (t is null) return;
        c.Tags = t;
        _library.Update(c);
        OnPropertyChanged(nameof(TagsText));
    }

    public void SetAlbum(AlbumViewModel? album)
    {
        if (Current is not { } c) return;
        c.AlbumId = album?.Id;
        _library.SetClipAlbum(c.Id, album?.Id);
    }

    [RelayCommand]
    private void Delete()
    {
        if (Current is not { } c) return;
        if (MessageBox.Show($"Delete “{c.Title}”?\nThis removes the file from disk.", "Delete clip",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _library.Delete(c.Id);
        try { if (File.Exists(c.FilePath)) File.Delete(c.FilePath); } catch { }
        int i = Index;
        Clips.Remove(c);
        if (Clips.Count == 0) { RequestClose?.Invoke(); return; }
        Index = Math.Min(i, Clips.Count - 1);
        NotifyAll();
        SourceChanged?.Invoke();
    }
}
