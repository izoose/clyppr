namespace Clipper.Core;

/// <summary>Standard filesystem locations for Clipper.</summary>
public static class AppPaths
{
    public static string DataRoot { get; }
    public static string DbPath => Path.Combine(DataRoot, "clipper.db");
    public static string SettingsPath => Path.Combine(DataRoot, "settings.json");
    public static string ThumbnailsDir { get; }
    public static string DefaultClipsDir { get; }

    static AppPaths()
    {
        DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clipper");
        ThumbnailsDir = Path.Combine(DataRoot, "thumbnails");
        DefaultClipsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Clipper");
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(ThumbnailsDir);
    }
}
