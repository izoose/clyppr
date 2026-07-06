using System.Diagnostics;
using Clipper.Engine;

// M1 test harness: record 12s of the desktop + 3 audio tracks into one MP4.
// An ffplay 440Hz tone stands in for "Discord" so we can prove per-track separation:
//   Desktop  = everything EXCEPT ffplay  (tone should be ABSENT)
//   AppAudio = ffplay only               (tone should be PRESENT)
//   Mic      = default microphone

Console.WriteLine("M1 recorder test: launching ffplay tone (stands in for Discord)...");
var player = Process.Start(new ProcessStartInfo
{
    FileName = "ffplay",
    Arguments = "-nodisp -autoexit -loglevel quiet -f lavfi -i sine=frequency=440:duration=20",
    UseShellExecute = false,
})!;
Thread.Sleep(1000); // let ffplay register + open its audio device

var cfg = new RecorderConfig
{
    OutputDirectory = "out",
    Fps = 60,
    Tracks = new()
    {
        new() { Name = "Desktop",  Kind = CaptureKind.ProcessExclude, ProcessName = "ffplay" },
        new() { Name = "AppAudio", Kind = CaptureKind.ProcessInclude, ProcessName = "ffplay" },
        new() { Name = "Mic",      Kind = CaptureKind.Mic },
    },
};

using var rec = new Recorder(cfg);
rec.Start();
Console.WriteLine($"recording -> {rec.CurrentFile}");
Console.WriteLine($"tracks: {string.Join(", ", rec.TrackNames)}");
Thread.Sleep(12_000);
string file = rec.Stop();
try { player.Kill(); } catch { }

Console.WriteLine($"done -> {file}");
var log = rec.FfmpegLog;
if (!string.IsNullOrWhiteSpace(log))
{
    Console.WriteLine("--- ffmpeg log ---");
    Console.WriteLine(log);
}
