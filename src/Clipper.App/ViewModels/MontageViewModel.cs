using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Clipper.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipper.App;

public partial class MontageItemViewModel : ObservableObject
{
    public string FilePath { get; }
    public string Title { get; }
    [ObservableProperty] private string? _thumbnailPath;

    public MontageItemViewModel(string filePath, string? thumb = null)
    {
        FilePath = filePath;
        Title = Path.GetFileNameWithoutExtension(filePath);
        _thumbnailPath = thumb;
    }
}

public partial class MontageViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ClipLibrary _library;
    private readonly Action<Clip> _onExported;

    public ObservableCollection<MontageItemViewModel> Items { get; } = new();

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _exporting;

    public MontageViewModel(AppSettings settings, ClipLibrary library, Action<Clip> onExported, IEnumerable<Clip>? initial = null)
    {
        _settings = settings;
        _library = library;
        _onExported = onExported;
        if (initial is not null)
            foreach (var c in initial) Items.Add(new MontageItemViewModel(c.FilePath, c.ThumbnailPath));
    }

    [RelayCommand]
    private void AddClips()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Add clips to montage",
            Filter = "Videos|*.mp4;*.mkv;*.mov;*.webm;*.avi|All files|*.*",
            Multiselect = true,
            InitialDirectory = _settings.ClipsDirectory,
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var f in dlg.FileNames)
        {
            var item = new MontageItemViewModel(f);
            Items.Add(item);
            _ = Task.Run(() => { var t = Thumbnailer.Generate(f); if (t is not null) Application.Current.Dispatcher.Invoke(() => item.ThumbnailPath = t); });
        }
    }

    [RelayCommand]
    private void MoveUp(MontageItemViewModel? item)
    {
        if (item is null) return;
        int i = Items.IndexOf(item);
        if (i > 0) Items.Move(i, i - 1);
    }

    [RelayCommand]
    private void MoveDown(MontageItemViewModel? item)
    {
        if (item is null) return;
        int i = Items.IndexOf(item);
        if (i >= 0 && i < Items.Count - 1) Items.Move(i, i + 1);
    }

    [RelayCommand]
    private void Remove(MontageItemViewModel? item)
    {
        if (item is not null) Items.Remove(item);
    }

    [RelayCommand]
    private async Task Export()
    {
        if (Exporting) return;
        if (Items.Count < 2) { Status = "Add at least 2 clips."; return; }
        Exporting = true;
        Status = "Preparing…";

        string outPath = Path.Combine(_settings.ClipsDirectory, $"montage_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        var paths = Items.Select(i => i.FilePath).ToList();
        try
        {
            await Task.Run(() => MontageExporter.Export(paths, outPath,
                progress: s => Application.Current.Dispatcher.Invoke(() => Status = s), cq: _settings.Cq));
            var clip = ClipImporter.Import(outPath, _library, title: $"Montage — {DateTime.Now:MMM d}");
            _onExported(clip);
            Status = "Montage exported ✓";
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
        finally { Exporting = false; }
    }
}
