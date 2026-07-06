using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Clipper.Core;

/// <summary>Reads video metadata via ffprobe (JSON output).</summary>
public static class FfProbe
{
    public sealed record Info(long DurationMs, int Width, int Height, IReadOnlyList<string> AudioTracks);

    public static Info? Read(string file, string ffprobePath = "ffprobe")
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v error -print_format json -show_format -show_streams \"{file}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            string json = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (string.IsNullOrWhiteSpace(json)) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            long durationMs = 0;
            if (root.TryGetProperty("format", out var fmt) &&
                fmt.TryGetProperty("duration", out var dur) &&
                double.TryParse(dur.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double secs))
                durationMs = (long)(secs * 1000);

            int w = 0, h = 0;
            var tracks = new List<string>();
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var s in streams.EnumerateArray())
                {
                    string type = s.TryGetProperty("codec_type", out var ct) ? ct.GetString() ?? "" : "";
                    if (type == "video" && w == 0)
                    {
                        if (s.TryGetProperty("width", out var wp)) w = wp.GetInt32();
                        if (s.TryGetProperty("height", out var hp)) h = hp.GetInt32();
                    }
                    else if (type == "audio")
                    {
                        string name = "Audio";
                        if (s.TryGetProperty("tags", out var tags) && tags.TryGetProperty("handler_name", out var hn))
                        {
                            var v = hn.GetString();
                            if (!string.IsNullOrWhiteSpace(v) && v != "SoundHandler") name = v!;
                        }
                        tracks.Add(name);
                    }
                }
            }
            return new Info(durationMs, w, h, tracks);
        }
        catch { return null; }
    }
}
