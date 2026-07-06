using System.Diagnostics;
using System.Runtime.InteropServices;

Directory.CreateDirectory("out");
var fmt = ProcessLoopbackCapture.Format;

uint targetPid;
bool selfTest = args.Length == 0;
string tonePath = Path.GetFullPath("out/tone.wav");
Process? player = null;

if (selfTest)
{
    // Spawn ffplay as the audio source and target ITS pid — mirrors the real use case
    // (capture another app's audio by process id). A 440Hz tone plays for ~10s.
    WriteTone(tonePath, seconds: 15, freq: 440, fmt);
    Console.WriteLine("SELF-TEST: launching ffplay to play a 440Hz tone; capturing it by pid.");
    Console.WriteLine("A beep will play for ~10s — this is the audio being separated.");
    player = Process.Start(new ProcessStartInfo
    {
        FileName = "ffplay",
        Arguments = $"-nodisp -autoexit -loglevel quiet \"{tonePath}\"",
        UseShellExecute = false,
    })!;
    Thread.Sleep(700);   // let ffplay open its audio device
    targetPid = (uint)player.Id;
    Console.WriteLine($"ffplay pid = {targetPid}");
}
else
{
    var procs = Process.GetProcessesByName(args[0]);
    if (procs.Length == 0) { Console.Error.WriteLine($"No process named '{args[0]}' running."); return 1; }
    targetPid = (uint)procs[0].Id;
    Console.WriteLine($"EXTERNAL: targeting '{args[0]}' pid {targetPid}. Make it play audio now (10s).");
}

using var incW = new WavWriter("out/included.wav", fmt.SampleRate, fmt.Channels, fmt.BitsPerSample);
using var excW = new WavWriter("out/excluded.wav", fmt.SampleRate, fmt.Channels, fmt.BitsPerSample);

var inc = new ProcessLoopbackCapture(targetPid, LoopbackMode.IncludeTree);
var exc = new ProcessLoopbackCapture(targetPid, LoopbackMode.ExcludeTree);
long incBytes = 0, excBytes = 0;
inc.DataAvailable += b => { incW.Write(b); Interlocked.Add(ref incBytes, b.Length); };
exc.DataAvailable += b => { excW.Write(b); Interlocked.Add(ref excBytes, b.Length); };

try
{
    inc.Start();
    exc.Start();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"capture start failed: {ex.GetType().Name} 0x{ex.HResult:X8} — {ex.Message}");
    try { player?.Kill(); } catch { }
    return 1;
}

Console.WriteLine("capturing 10s...");
Thread.Sleep(10_000);

inc.Stop();
exc.Stop();
try { player?.Kill(); } catch { }
inc.Dispose();
exc.Dispose();

Console.WriteLine($"done -> out/included.wav ({incBytes / 1024} KB), out/excluded.wav ({excBytes / 1024} KB)");
return 0;

// ---- helpers ----

static void WriteTone(string path, int seconds, double freq, AudioFormat f)
{
    using var w = new WavWriter(path, f.SampleRate, f.Channels, f.BitsPerSample);
    int total = f.SampleRate * seconds;
    var buf = new byte[total * f.BlockAlign];
    int i = 0;
    for (int n = 0; n < total; n++)
    {
        double t = (double)n / f.SampleRate;
        short s = (short)(Math.Sin(2 * Math.PI * freq * t) * 0.25 * short.MaxValue);
        for (int c = 0; c < f.Channels; c++) { buf[i++] = (byte)(s & 0xFF); buf[i++] = (byte)((s >> 8) & 0xFF); }
    }
    w.Write(buf);
}
