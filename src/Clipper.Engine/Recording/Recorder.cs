using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Clipper.Engine;

/// <summary>Describes one audio track to record.</summary>
public sealed class AudioTrackConfig
{
    public required string Name { get; init; }
    public required CaptureKind Kind { get; init; }
    /// <summary>Process name (no ".exe") for ProcessInclude/ProcessExclude.</summary>
    public string? ProcessName { get; init; }
}

public sealed class RecorderConfig
{
    public int Fps { get; init; } = 60;
    public required string OutputDirectory { get; init; }
    public List<AudioTrackConfig> Tracks { get; init; } = DefaultTracks();
    public string FfmpegPath { get; init; } = "ffmpeg";
    public string NvencPreset { get; init; } = "p5";
    /// <summary>NVENC constant-quality level (lower = better/bigger). ~19-23 is a good range.</summary>
    public int Cq { get; init; } = 21;

    /// <summary>Default track layout: desktop-minus-voice, voice-only, mic.</summary>
    public static List<AudioTrackConfig> DefaultTracks(string voiceApp = "Discord") => new()
    {
        new() { Name = "Desktop", Kind = CaptureKind.ProcessExclude, ProcessName = voiceApp },
        new() { Name = "Voice",   Kind = CaptureKind.ProcessInclude, ProcessName = voiceApp },
        new() { Name = "Mic",     Kind = CaptureKind.Mic },
    };
}

/// <summary>
/// Records the primary display plus one audio track per configured source into a single MP4
/// (1 video stream + N audio streams), muxed by ffmpeg fed through named pipes. NVENC encoded.
/// </summary>
public sealed class Recorder : IDisposable
{
    private readonly RecorderConfig _cfg;
    private ICapture? _video;
    private readonly List<(AudioTrackConfig cfg, AudioCapture cap, NamedPipeServerStream pipe)> _audio = new();
    private NamedPipeServerStream? _videoPipe;
    private Process? _ffmpeg;
    private Thread? _videoThread;
    private readonly StringBuilder _ffmpegErr = new();
    private volatile bool _running;
    private byte[]? _latestFrame;
    private readonly object _frameLock = new();

    public bool IsRecording => _running;
    public string? CurrentFile { get; private set; }
    /// <summary>The final ordered track names actually recorded (video first is implicit).</summary>
    public IReadOnlyList<string> TrackNames => _audio.Select(a => a.cfg.Name).ToList();

    public Recorder(RecorderConfig cfg) => _cfg = cfg;

    public void Start()
    {
        if (_running) throw new InvalidOperationException("Already recording.");
        Directory.CreateDirectory(_cfg.OutputDirectory);
        string id = Guid.NewGuid().ToString("N")[..8];
        CurrentFile = Path.Combine(_cfg.OutputDirectory, $"clip_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

        // 1. Start video capture (WGC preferred, DXGI fallback) to learn dimensions.
        _video = StartVideo();
        _video.FrameBgra += OnFrame;
        int w = _video.Width, h = _video.Height;

        // 2. Resolve + create audio captures.
        BuildAudioCaptures(id);

        // 3. Create the video pipe and launch ffmpeg.
        string vPipeName = $"clipper_v_{id}";
        _videoPipe = NewPipe(vPipeName, 16 * 1024 * 1024);
        _ffmpeg = LaunchFfmpeg(w, h, vPipeName, id);

        // 4. ffmpeg opens inputs sequentially and blocks reading each one to finish opening it
        //    before opening the next — so we must feed data to each input as soon as it connects,
        //    otherwise ffmpeg never advances to open the following pipe (deadlock).
        _running = true;

        WaitConnect(_videoPipe, "video");
        _videoThread = new Thread(VideoPump) { IsBackground = true, Name = "video-pump" };
        _videoThread.Start();

        foreach (var a in _audio)
        {
            WaitConnect(a.pipe, a.cfg.Name);            // ffmpeg has now opened this audio input
            var pipe = a.pipe;                          // one writer per pipe = its own capture thread
            a.cap.DataAvailable += buf => { try { pipe.Write(buf, 0, buf.Length); } catch { } };
            a.cap.Start();
        }
    }

    private ICapture StartVideo()
    {
        try
        {
            var wgc = new WgcCapture();
            wgc.Start();
            return wgc;
        }
        catch
        {
            var dxgi = new DxgiCapture();
            dxgi.Start();
            return dxgi;
        }
    }

    private void BuildAudioCaptures(string id)
    {
        int i = 0;
        foreach (var t in _cfg.Tracks)
        {
            CaptureKind kind = t.Kind;
            uint pid = 0;
            if (t.Kind is CaptureKind.ProcessInclude or CaptureKind.ProcessExclude)
            {
                var proc = Process.GetProcessesByName(t.ProcessName).FirstOrDefault();
                if (proc is null)
                {
                    if (t.Kind == CaptureKind.ProcessInclude)
                        continue;                       // can't capture a non-running app — drop the track
                    kind = CaptureKind.SystemAudio;     // "desktop minus X" with no X → all system audio
                }
                else pid = (uint)proc.Id;
            }

            var cap = new AudioCapture(kind, pid);
            var pipe = NewPipe($"clipper_a{i}_{id}", 4 * 1024 * 1024);
            _audio.Add((t, cap, pipe));
            i++;
        }
    }

    private static NamedPipeServerStream NewPipe(string name, int outBuffer) =>
        new(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, outBuffer);

    private void WaitConnect(NamedPipeServerStream pipe, string label)
    {
        if (!pipe.WaitForConnectionAsync().Wait(8000))
            throw new IOException($"ffmpeg did not connect to the '{label}' pipe.\n{FfmpegLog}");
    }

    private Process LaunchFfmpeg(int w, int h, string vPipeName, string id)
    {
        var sb = new StringBuilder();
        sb.Append("-y -hide_banner -loglevel warning ");
        sb.Append($"-f rawvideo -pixel_format bgra -video_size {w}x{h} -framerate {_cfg.Fps} -i \\\\.\\pipe\\{vPipeName} ");
        for (int i = 0; i < _audio.Count; i++)
            sb.Append($"-f s16le -ar {AudioCapture.Format.SampleRate} -ac {AudioCapture.Format.Channels} -i \\\\.\\pipe\\clipper_a{i}_{id} ");
        sb.Append("-map 0:v ");
        for (int i = 0; i < _audio.Count; i++) sb.Append($"-map {i + 1}:a ");
        sb.Append($"-c:v h264_nvenc -preset {_cfg.NvencPreset} -rc vbr -cq {_cfg.Cq} -pix_fmt yuv420p -g {_cfg.Fps * 2} ");
        sb.Append("-c:a aac -b:a 160k ");
        // MP4 has no standard per-stream 'title' tag (ffmpeg drops it); handler_name persists,
        // so players show something. The app's library DB holds the authoritative track map.
        for (int i = 0; i < _audio.Count; i++)
            sb.Append($"-metadata:s:a:{i} handler_name=\"{_audio[i].cfg.Name}\" ");
        sb.Append($"\"{CurrentFile}\"");

        var psi = new ProcessStartInfo
        {
            FileName = _cfg.FfmpegPath,
            Arguments = sb.ToString(),
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var proc = Process.Start(psi)!;
        _ = Task.Run(() =>
        {
            string? line;
            while ((line = proc.StandardError.ReadLine()) is not null)
            {
                lock (_ffmpegErr) { _ffmpegErr.AppendLine(line); if (_ffmpegErr.Length > 16000) _ffmpegErr.Remove(0, _ffmpegErr.Length - 16000); }
            }
        });
        return proc;
    }

    private void OnFrame(byte[] buf)
    {
        lock (_frameLock) _latestFrame = buf;
    }

    private void VideoPump()
    {
        double frameMs = 1000.0 / _cfg.Fps;
        var clock = Stopwatch.StartNew();
        long i = 0;
        var pipe = _videoPipe!;
        while (_running)
        {
            double due = i * frameMs;
            double wait = due - clock.Elapsed.TotalMilliseconds;
            if (wait > 1) Thread.Sleep((int)wait);
            i++;

            byte[]? frame;
            lock (_frameLock) frame = _latestFrame;
            if (frame is null) continue;
            try { pipe.Write(frame, 0, frame.Length); }
            catch { break; }
        }
    }

    public string Stop()
    {
        if (!_running) return CurrentFile ?? "";
        _running = false;

        _videoThread?.Join(2000);
        try { _video?.Stop(); } catch { }
        foreach (var a in _audio) { try { a.cap.Stop(); } catch { } }

        // Close pipes → ffmpeg reads EOF on every input and finalizes the mp4.
        try { _videoPipe?.Dispose(); } catch { }
        foreach (var a in _audio) { try { a.pipe.Dispose(); } catch { } }

        if (_ffmpeg is not null)
        {
            if (!_ffmpeg.WaitForExit(15000))
            {
                try { _ffmpeg.Kill(); } catch { }
            }
        }
        return CurrentFile ?? "";
    }

    /// <summary>ffmpeg stderr tail — for diagnostics if a recording looks wrong.</summary>
    public string FfmpegLog { get { lock (_ffmpegErr) return _ffmpegErr.ToString(); } }

    public void Dispose()
    {
        if (_running) Stop();
        _video?.Dispose();
        foreach (var a in _audio) a.cap.Dispose();
    }
}
