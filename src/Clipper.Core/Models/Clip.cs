using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Clipper.Core;

/// <summary>A recorded/imported clip and its metadata (one row in the library DB).</summary>
public sealed class Clip : INotifyPropertyChanged
{
    public long Id { get; set; }
    public string FilePath { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Game { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public long DurationMs { get; set; }
    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? ThumbnailPath { get; set; }
    public string? ShareUrl { get; set; }
    /// <summary>Album this clip belongs to, or null for "unsorted".</summary>
    public long? AlbumId { get; set; }
    public bool IsFavorite { get; set; }
    /// <summary>Comma-separated audio track names, in stream order (e.g. "Desktop,Voice,Mic").</summary>
    public string Tracks { get; set; } = "";
    /// <summary>Comma-separated free-form tags.</summary>
    public string Tags { get; set; } = "";
    /// <summary>Detected player usernames (comma-separated). null = not scanned yet; "" = scanned, none.</summary>
    public string? Players { get; set; }

    public IReadOnlyList<string> TrackList =>
        Tracks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string DurationText
    {
        get
        {
            var t = TimeSpan.FromMilliseconds(DurationMs);
            return DurationMs >= 3_600_000 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        }
    }

    public string SizeText => SizeBytes >= 1_000_000_000
        ? $"{SizeBytes / 1_073_741_824.0:0.0} GB"
        : $"{SizeBytes / 1_048_576.0:0.0} MB";

    public string TimeAgo
    {
        get
        {
            var d = DateTime.Now - CreatedAt;
            if (d.TotalSeconds < 60) return "just now";
            if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} minute{Plural((int)d.TotalMinutes)} ago";
            if (d.TotalHours < 24) return $"{(int)d.TotalHours} hour{Plural((int)d.TotalHours)} ago";
            if (d.TotalDays < 30) return $"{(int)d.TotalDays} day{Plural((int)d.TotalDays)} ago";
            return CreatedAt.ToString("MMM d");
        }
    }

    private static string Plural(int n) => n == 1 ? "" : "s";

    // ---- transient UI state (not persisted) ----
    private bool _isSelected;
    /// <summary>Whether this clip is ticked in a multi-select. Runtime only, never saved.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
