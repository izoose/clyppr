using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Clipper.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipper.App;

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
