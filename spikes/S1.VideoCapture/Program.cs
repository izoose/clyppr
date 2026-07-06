using System.Diagnostics;
using System.Text;

Console.WriteLine("S1 spike: WGC capture -> h264_nvenc -> out/spike.mp4");

byte[]? latest = null;
var latestLock = new object();

// Try WGC first (better for true-fullscreen games); fall back to DXGI Desktop
// Duplication (works without the per-user CaptureService, e.g. on debloated PCs).
ICapture capture;
try
{
    capture = new WgcCapture();
    capture.FrameBgra += buf => { lock (latestLock) latest = buf; };
    capture.Start();
    Console.WriteLine($"using WGC, capturing primary monitor at {capture.Width}x{capture.Height}");
}
catch (Exception ex)
{
    Console.WriteLine($"WGC unavailable ({ex.GetType().Name}: 0x{ex.HResult:X8}) — falling back to DXGI Desktop Duplication");
    capture = new DxgiCapture();
    capture.FrameBgra += buf => { lock (latestLock) latest = buf; };
    capture.Start();
    Console.WriteLine($"using DXGI, capturing primary output at {capture.Width}x{capture.Height}");
}

// Wait for the first real frame (proves capture is actually delivering).
var firstFrameWait = Stopwatch.StartNew();
while (true)
{
    lock (latestLock) if (latest is not null) break;
    if (firstFrameWait.Elapsed > TimeSpan.FromSeconds(3))
    {
        Console.Error.WriteLine("ERROR: no frames arrived within 3s — WGC not delivering.");
        capture.Stop();
        return 1;
    }
    Thread.Sleep(10);
}

Directory.CreateDirectory("out");
string outPath = "out/spike.mp4";
string ffArgs = $"-y -f rawvideo -pixel_format bgra -video_size {capture.Width}x{capture.Height} " +
              $"-framerate 60 -i - -c:v h264_nvenc -preset p5 -pix_fmt yuv420p \"{outPath}\"";

using var proc = Process.Start(Ffmpeg.Start(ffArgs))!;
var stdin = proc.StandardInput.BaseStream;

// Drain ffmpeg stderr so its pipe never blocks; keep the tail for diagnostics.
var errTail = new StringBuilder();
_ = Task.Run(() =>
{
    string? line;
    while ((line = proc.StandardError.ReadLine()) is not null)
    {
        errTail.AppendLine(line);
        if (errTail.Length > 8000) errTail.Remove(0, errTail.Length - 8000);
    }
});

// Pace a constant 60fps feed for ~10 seconds (re-send latest captured frame each tick).
const int fps = 60;
const int totalFrames = fps * 10;
double frameMs = 1000.0 / fps;
var clock = Stopwatch.StartNew();
int written = 0;
for (int i = 0; i < totalFrames; i++)
{
    double due = i * frameMs;
    double wait = due - clock.Elapsed.TotalMilliseconds;
    if (wait > 1) Thread.Sleep((int)wait);

    byte[]? frame;
    lock (latestLock) frame = latest;
    if (frame is null) continue;
    try { stdin.Write(frame, 0, frame.Length); written++; }
    catch (Exception ex) { Console.Error.WriteLine($"stdin write failed: {ex.Message}"); break; }
}

capture.Stop();
stdin.Close();          // EOF → ffmpeg finalizes the mp4
proc.WaitForExit();

Console.WriteLine($"fed {written}/{totalFrames} frames, ffmpeg exit {proc.ExitCode}");
if (proc.ExitCode != 0)
{
    Console.Error.WriteLine("--- ffmpeg stderr tail ---");
    Console.Error.WriteLine(errTail.ToString());
    return 1;
}

var fi = new FileInfo(outPath);
Console.WriteLine($"done -> {fi.FullName} ({fi.Length / 1024} KB)");
return 0;
