using System.Diagnostics;
using System.Runtime.InteropServices;
using Clipper.Engine;

// Modes:  "record" = M1 single-file recorder   |   "buffer" (default) = M2 replay buffer + hotkey
string mode = args.Length > 0 ? args[0] : "buffer";
Directory.CreateDirectory("out");

Console.WriteLine($"[{mode}] launching ffplay tone (stands in for Discord)...");
var player = Process.Start(new ProcessStartInfo
{
    FileName = "ffplay",
    // -volume 8 keeps this near-silent; separation is already proven, so we don't need it loud.
    Arguments = "-volume 8 -nodisp -autoexit -loglevel quiet -f lavfi -i sine=frequency=440:duration=30",
    UseShellExecute = false,
})!;
Thread.Sleep(1000);

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

if (mode == "record")
{
    using var rec = new Recorder(cfg);
    rec.Start();
    Console.WriteLine($"recording -> {rec.CurrentFile} [{string.Join(", ", rec.TrackNames)}]");
    Thread.Sleep(10_000);
    Console.WriteLine($"done -> {rec.Stop()}");
}
else
{
    using var buffer = new ReplayBuffer(cfg, maxBufferSeconds: 30);
    buffer.Start();
    Console.WriteLine($"buffering (tracks: {string.Join(", ", buffer.TrackNames)}) ... building 15s of buffer");

    string? saved = null;
    var done = new ManualResetEventSlim(false);
    using var hk = new GlobalHotkey(HotkeyModifiers.Alt, 0x43 /* 'C' */);
    hk.Pressed += () =>
    {
        Console.WriteLine("ALT+C pressed -> saving last 10s...");
        saved = buffer.SaveLast(10, Path.GetFullPath("out/replay.mp4"));
        done.Set();
    };
    Console.WriteLine($"hotkey ALT+C registered: {hk.Start()}");

    Thread.Sleep(15_000);
    Console.WriteLine("simulating ALT+C keypress...");
    SendAltC();
    done.Wait(8000);
    buffer.Stop();
    Console.WriteLine($"saved -> {saved ?? "(nothing)"}");
    if (!string.IsNullOrWhiteSpace(buffer.FfmpegLog))
        Console.WriteLine("ffmpeg log:\n" + buffer.FfmpegLog);
}

try { player.Kill(); } catch { }

static void SendAltC()
{
    const byte VK_MENU = 0x12, VK_C = 0x43;
    const uint KEYUP = 0x0002;
    keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
    keybd_event(VK_C, 0, 0, UIntPtr.Zero);
    Thread.Sleep(60);
    keybd_event(VK_C, 0, KEYUP, UIntPtr.Zero);
    keybd_event(VK_MENU, 0, KEYUP, UIntPtr.Zero);
}

[DllImport("user32.dll")]
static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
