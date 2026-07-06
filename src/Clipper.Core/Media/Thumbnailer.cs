using System.Diagnostics;

namespace Clipper.Core;

/// <summary>Generates a JPG thumbnail for a clip via ffmpeg.</summary>
public static class Thumbnailer
{
    /// <summary>Grabs a frame ~<paramref name="atSeconds"/> in, scaled to 480px wide. Returns the path or null.</summary>
    public static string? Generate(string videoFile, double atSeconds = 1.0, string ffmpegPath = "ffmpeg")
    {
        try
        {
            string outPath = Path.Combine(AppPaths.ThumbnailsDir, $"{Guid.NewGuid():N}.jpg");
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-y -v error -ss {atSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                            $"-i \"{videoFile}\" -frames:v 1 -vf scale=480:-2 -q:v 4 \"{outPath}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return File.Exists(outPath) ? outPath : null;
        }
        catch { return null; }
    }
}
