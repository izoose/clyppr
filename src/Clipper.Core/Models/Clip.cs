namespace Clipper.Core;

/// <summary>A recorded/imported clip and its metadata (one row in the library DB).</summary>
public sealed class Clip
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
}
