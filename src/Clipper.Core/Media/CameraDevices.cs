using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Clipper.Core;

/// <summary>Enumerates DirectShow video capture devices (webcams) via ffmpeg.</summary>
public static class CameraDevices
{
    public static List<string> List(string ffmpegPath = "ffmpeg")
    {
        var names = new List<string>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-hide_banner -list_devices true -f dshow -i dummy",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();

            // Lines look like:  [dshow @ ...] "Integrated Camera" (video)
            // Virtual cams (OBS/Streamlabs) can report "(none)" when idle — include those too.
            foreach (Match m in Regex.Matches(err, "\"([^\"]+)\"\\s*\\((video|none)\\)"))
                names.Add(m.Groups[1].Value);
        }
        catch { }
        return names;
    }
}
