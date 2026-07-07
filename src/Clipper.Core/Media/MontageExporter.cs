using System.Diagnostics;

namespace Clipper.Core;

/// <summary>Stitches multiple clips into one video: normalizes each to a common format
/// (1080p30, stereo AAC) then concatenates. Handles clips of differing size/fps/audio.</summary>
public static class MontageExporter
{
    public static void Export(IReadOnlyList<string> inputs, string output,
        Action<string>? progress = null, string ffmpegPath = "ffmpeg", string nvencPreset = "p5", int cq = 21)
    {
        if (inputs.Count == 0) throw new ArgumentException("No clips to stitch.");

        string workDir = Path.Combine(Path.GetTempPath(), "clipper_montage_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workDir);
        var temps = new List<string>();
        try
        {
            const string vf = "scale=1920:1080:force_original_aspect_ratio=decrease," +
                              "pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30";
            string venc = $"-c:v h264_nvenc -preset {nvencPreset} -rc vbr -cq {cq} -pix_fmt yuv420p";

            for (int i = 0; i < inputs.Count; i++)
            {
                progress?.Invoke($"Normalizing {i + 1}/{inputs.Count}…");
                string temp = Path.Combine(workDir, $"part_{i:D3}.mp4");
                bool hasAudio = (FfProbe.Read(inputs[i], ffmpegPath.Replace("ffmpeg", "ffprobe"))?.AudioTracks.Count ?? 0) > 0;

                string args = hasAudio
                    ? $"-y -hide_banner -loglevel error -i \"{inputs[i]}\" -map 0:v:0 -map 0:a:0 " +
                      $"-vf \"{vf}\" {venc} -c:a aac -ar 48000 -ac 2 \"{temp}\""
                    : $"-y -hide_banner -loglevel error -i \"{inputs[i]}\" -f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000 " +
                      $"-map 0:v:0 -map 1:a -shortest -vf \"{vf}\" {venc} -c:a aac -ar 48000 -ac 2 \"{temp}\"";

                Run(ffmpegPath, args);
                temps.Add(temp);
            }

            progress?.Invoke("Stitching…");
            string listPath = Path.Combine(workDir, "list.txt");
            File.WriteAllLines(listPath, temps.Select(t => $"file '{t.Replace('\\', '/')}'"));
            Run(ffmpegPath, $"-y -hide_banner -loglevel error -f concat -safe 0 -i \"{listPath}\" -c copy -movflags +faststart \"{output}\"");

            if (!File.Exists(output)) throw new InvalidOperationException("Montage produced no output.");
        }
        finally
        {
            try { Directory.Delete(workDir, true); } catch { }
        }
    }

    private static void Run(string exe, string args)
    {
        var psi = new ProcessStartInfo { FileName = exe, Arguments = args, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("ffmpeg failed: " + err);
    }
}
