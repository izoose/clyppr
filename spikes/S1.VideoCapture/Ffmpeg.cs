using System.Diagnostics;

static class Ffmpeg
{
    // ffmpeg 8.1 is on PATH on the target machine; resolve by name.
    public const string Exe = "ffmpeg";
    public const string Probe = "ffprobe";

    public static ProcessStartInfo Start(string args) => new()
    {
        FileName = Exe,
        Arguments = args,
        RedirectStandardInput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
}
