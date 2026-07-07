using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Clipper.Core;

/// <summary>Trims a clip and flattens the chosen audio tracks (with per-track volume) into a
/// single stereo track via ffmpeg — the "cut Discord, keep game" export.</summary>
public static class ClipExporter
{
    public sealed record TrackChoice(int Index, bool Keep, double Volume);

    /// <summary>
    /// Exports [<paramref name="inPoint"/>, <paramref name="outPoint"/>] of <paramref name="input"/>
    /// to <paramref name="output"/>, keeping only the chosen audio tracks mixed to stereo.
    /// Video is re-encoded (NVENC) for frame-accurate trims. Throws on ffmpeg failure.
    /// </summary>
    public static void Export(
        string input, string output,
        TimeSpan inPoint, TimeSpan outPoint,
        IReadOnlyList<TrackChoice> tracks,
        string ffmpegPath = "ffmpeg", string nvencPreset = "p5", int cq = 21)
    {
        double start = Math.Max(0, inPoint.TotalSeconds);
        double dur = Math.Max(0.1, (outPoint - inPoint).TotalSeconds);
        string S(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        var kept = tracks.Where(t => t.Keep).ToList();

        var sb = new StringBuilder();
        sb.Append($"-y -hide_banner -loglevel error -ss {S(start)} -i \"{input}\" -t {S(dur)} ");

        if (kept.Count == 0)
        {
            sb.Append("-map 0:v -an ");
        }
        else if (kept.Count == 1)
        {
            sb.Append($"-filter_complex \"[0:a:{kept[0].Index}]volume={S(kept[0].Volume)}[aout]\" ");
            sb.Append("-map 0:v -map \"[aout]\" -c:a aac -b:a 192k ");
        }
        else
        {
            var fc = new StringBuilder();
            for (int i = 0; i < kept.Count; i++)
                fc.Append($"[0:a:{kept[i].Index}]volume={S(kept[i].Volume)}[a{i}];");
            for (int i = 0; i < kept.Count; i++) fc.Append($"[a{i}]");
            fc.Append($"amix=inputs={kept.Count}:normalize=0[aout]");
            sb.Append($"-filter_complex \"{fc}\" -map 0:v -map \"[aout]\" -c:a aac -b:a 192k ");
        }

        sb.Append($"-c:v h264_nvenc -preset {nvencPreset} -rc vbr -cq {cq} -pix_fmt yuv420p -movflags +faststart ");
        sb.Append($"\"{output}\"");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = sb.ToString(),
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0 || !File.Exists(output))
            throw new InvalidOperationException("Export failed: " + err);
    }

    /// <summary>Exports [in, out] as a high-quality GIF (2-pass palette in one filtergraph).</summary>
    public static void ExportGif(
        string input, string output, TimeSpan inPoint, TimeSpan outPoint,
        int fps = 15, int width = 480, string ffmpegPath = "ffmpeg")
    {
        double start = Math.Max(0, inPoint.TotalSeconds);
        double dur = Math.Max(0.1, (outPoint - inPoint).TotalSeconds);
        string S(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        string filter = $"fps={fps},scale={width}:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse";
        string args = $"-y -hide_banner -loglevel error -ss {S(start)} -t {S(dur)} -i \"{input}\" " +
                      $"-filter_complex \"{filter}\" -loop 0 \"{output}\"";

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0 || !File.Exists(output))
            throw new InvalidOperationException("GIF export failed: " + err);
    }
}
