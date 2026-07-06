using System.Collections.ObjectModel;
using System.IO;
using Clipper.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipper.App;

public partial class TrackRowViewModel : ObservableObject
{
    public int Index { get; init; }
    public string Name { get; init; } = "Audio";

    [ObservableProperty] private bool _keep = true;
    [ObservableProperty] private double _volume = 1.0;

    public string VolumeText => $"{Volume * 100:0}%";
    partial void OnVolumeChanged(double value) => OnPropertyChanged(nameof(VolumeText));
}

public partial class EditorViewModel : ObservableObject
{
    private readonly Clip _clip;
    private readonly AppSettings _settings;
    private readonly ClipLibrary _library;
    private readonly Action<Clip> _onExported;

    public string FilePath => _clip.FilePath;
    public string Title => _clip.Title;
    public ObservableCollection<TrackRowViewModel> Tracks { get; } = new();

    [ObservableProperty] private double _durationSeconds = 1;
    [ObservableProperty] private double _inSeconds;
    [ObservableProperty] private double _outSeconds = 1;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _exporting;

    public string InText => TimeSpan.FromSeconds(InSeconds).ToString(@"m\:ss\.f");
    public string OutText => TimeSpan.FromSeconds(OutSeconds).ToString(@"m\:ss\.f");
    public string SelectionText => $"{TimeSpan.FromSeconds(Math.Max(0, OutSeconds - InSeconds)):m\\:ss\\.f} selected";

    partial void OnInSecondsChanged(double value) { OnPropertyChanged(nameof(InText)); OnPropertyChanged(nameof(SelectionText)); }
    partial void OnOutSecondsChanged(double value) { OnPropertyChanged(nameof(OutText)); OnPropertyChanged(nameof(SelectionText)); }

    public EditorViewModel(Clip clip, AppSettings settings, ClipLibrary library, Action<Clip> onExported)
    {
        _clip = clip;
        _settings = settings;
        _library = library;
        _onExported = onExported;

        var names = clip.TrackList.Count > 0
            ? clip.TrackList.ToList()
            : (FfProbe.Read(clip.FilePath)?.AudioTracks.ToList() ?? new List<string>());
        for (int i = 0; i < names.Count; i++)
            Tracks.Add(new TrackRowViewModel { Index = i, Name = names[i] });
    }

    public void SetDuration(double seconds)
    {
        if (seconds <= 0) return;
        DurationSeconds = seconds;
        if (OutSeconds <= 1 || OutSeconds > seconds) OutSeconds = seconds;
    }

    public void SetIn(double pos) => InSeconds = Math.Clamp(pos, 0, Math.Max(0, OutSeconds - 0.1));
    public void SetOut(double pos) => OutSeconds = Math.Clamp(pos, InSeconds + 0.1, DurationSeconds);

    [RelayCommand]
    private async Task Export()
    {
        if (Exporting) return;
        Exporting = true;
        Status = "Exporting…";

        string dir = _settings.ClipsDirectory;
        string outPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(_clip.FilePath) + "_edit.mp4");
        int n = 1;
        while (File.Exists(outPath))
            outPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(_clip.FilePath) + $"_edit{n++}.mp4");

        var choices = Tracks.Select(t => new ClipExporter.TrackChoice(t.Index, t.Keep, t.Volume)).ToList();
        string keptNames = string.Join(",", Tracks.Where(t => t.Keep).Select(t => t.Name));

        try
        {
            await Task.Run(() => ClipExporter.Export(
                _clip.FilePath, outPath,
                TimeSpan.FromSeconds(InSeconds), TimeSpan.FromSeconds(OutSeconds),
                choices, cq: _settings.Cq));
            var clip = ClipImporter.Import(outPath, _library, tracks: keptNames, game: _clip.Game);
            _onExported(clip);
            Status = "Exported ✓";
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
        finally { Exporting = false; }
    }
}
