namespace Clipper.Core;

/// <summary>Probes a video file, makes a thumbnail, and adds it to the library.</summary>
public static class ClipImporter
{
    public static Clip Import(string filePath, ClipLibrary library, string? title = null, string tracks = "", string? game = null)
    {
        var info = FfProbe.Read(filePath);
        double thumbAt = Math.Max(0.5, (info?.DurationMs ?? 2000) / 2000.0);
        string? thumb = Thumbnailer.Generate(filePath, thumbAt);

        long size = 0;
        DateTime created = DateTime.Now;
        try { var fi = new FileInfo(filePath); size = fi.Length; created = fi.CreationTime; } catch { }

        var clip = new Clip
        {
            FilePath = filePath,
            Title = title ?? Path.GetFileNameWithoutExtension(filePath),
            Game = game,
            CreatedAt = created,
            DurationMs = info?.DurationMs ?? 0,
            SizeBytes = size,
            Width = info?.Width ?? 0,
            Height = info?.Height ?? 0,
            ThumbnailPath = thumb,
            Tracks = !string.IsNullOrEmpty(tracks) ? tracks : string.Join(",", info?.AudioTracks ?? new List<string>()),
        };
        library.Add(clip);
        return clip;
    }
}
