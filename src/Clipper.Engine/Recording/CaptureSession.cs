using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Clipper.Engine;

/// <summary>
/// Captures the primary display + one audio source per configured track and feeds them through
/// named pipes into a single ffmpeg process (NVENC video + AAC per track). Output-agnostic: the
/// caller supplies the ffmpeg sink (a single MP4 file, or a rotating segment muxer). Shared by
/// <see cref="Recorder"/> and <see cref="ReplayBuffer"/>.
/// </summary>
sealed class CaptureSession : IDisposable
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

    public bool IsRunning => _running;
    public IReadOnlyList<string> TrackNames => _audio.Select(a => a.cfg.Name).ToList();
    public string FfmpegLog { get { lock (_ffmpegErr) return _ffmpegErr.ToString(); } }

    public CaptureSession(RecorderConfig cfg) => _cfg = cfg;

    /// <param name="sinkArgs">ffmpeg output target + muxer options (the tail of the command).</param>
    /// <param name="extraEncoderArgs">Extra encoder flags (e.g. forced keyframes for segmenting).</param>
    public void Start(string sinkArgs, string extraEncoderArgs = "")
    {
        if (_running) throw new InvalidOperationException("Already running.");
        string id = Guid.NewGuid().ToString("N")[..8];

        _video = StartVideo();
        _video.FrameBgra += OnFrame;
        int w = _video.Width, h = _video.Height;

        BuildAudioCaptures(id);

        string vPipeName = $"clipper_v_{id}";
        _videoPipe = NewPipe(vPipeName, 16 * 1024 * 1024);
        _ffmpeg = LaunchFfmpeg(w, h, vPipeName, id, sinkArgs, extraEncoderArgs);

        // ffmpeg opens inputs sequentially and blocks reading each to finish opening it before
        // opening the next — feed each input as soon as it connects, else it deadlocks.
        _running = true;
        WaitConnect(_videoPipe, "video");
        _videoThread = new Thread(VideoPump) { IsBackground = true, Name = "video-pump" };
        _videoThread.Start();
        foreach (var a in _audio)
        {
            WaitConnect(a.pipe, a.cfg.Name);
            var pipe = a.pipe;
            a.cap.DataAvailable += buf => { try { pipe.Write(buf, 0, buf.Length); } catch { } };
            a.cap.Start();
        }
    }

    private ICapture StartVideo()
    {
        // GPU NV12 conversion only when there's no facecam overlay (that path composites in BGRA).
        bool wantNv12 = !(_cfg.FacecamEnabled && !string.IsNullOrWhiteSpace(_cfg.FacecamDevice));
        try { var wgc = new WgcCapture(preferNv12: wantNv12, fps: _cfg.Fps); wgc.Start(); return wgc; }
        catch { var dxgi = new DxgiCapture(); dxgi.Start(); return dxgi; }
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
                if (t.Pid is uint givenPid) pid = givenPid;               // exact process id (per-app separation)
                else
                {
                    var proc = Process.GetProcessesByName(t.ProcessName).FirstOrDefault();
                    if (proc is null)
                    {
                        if (t.Kind == CaptureKind.ProcessInclude) continue;   // no such app → drop track
                        kind = CaptureKind.SystemAudio;                        // "desktop minus X", no X → all audio
                    }
                    else pid = (uint)proc.Id;
                }
            }
            var cap = new AudioCapture(kind, pid, t.DeviceId);
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

    private Process LaunchFfmpeg(int w, int h, string vPipeName, string id, string sinkArgs, string extraEncoderArgs)
    {
        bool nv12 = _video?.Format == CaptureFormat.Nv12;

        var sb = new StringBuilder();
        sb.Append("-y -hide_banner -loglevel warning ");
        sb.Append($"-f rawvideo -pixel_format {(nv12 ? "nv12" : "bgra")} -video_size {w}x{h} -framerate {_cfg.Fps} -i \\\\.\\pipe\\{vPipeName} ");
        for (int i = 0; i < _audio.Count; i++)
            sb.Append($"-f s16le -ar {AudioCapture.Format.SampleRate} -ac {AudioCapture.Format.Channels} -i \\\\.\\pipe\\clipper_a{i}_{id} ");

        bool facecam = _cfg.FacecamEnabled && !string.IsNullOrWhiteSpace(_cfg.FacecamDevice);
        if (facecam)
            sb.Append($"-f dshow -rtbufsize 128M -i video=\"{_cfg.FacecamDevice}\" ");

        if (facecam)
        {
            int cam = _audio.Count + 1;
            string pos = _cfg.FacecamCorner switch
            {
                "BottomLeft" => "20:H-h-20",
                "TopRight" => "W-w-20:20",
                "TopLeft" => "20:20",
                _ => "W-w-20:H-h-20",
            };
            sb.Append($"-filter_complex \"[{cam}:v]scale={_cfg.FacecamWidth}:-1,setsar=1[cam];[0:v][cam]overlay={pos},scale=in_range=full:out_range=tv[vout]\" -map \"[vout]\" ");
        }
        // NV12 frames are already BT.709 limited-range (converted on the GPU) → no CPU scale needed.
        else if (nv12) sb.Append("-map 0:v ");
        else sb.Append("-map 0:v -vf \"scale=in_range=full:out_range=tv\" ");
        for (int i = 0; i < _audio.Count; i++) sb.Append($"-map {i + 1}:a ");
        // Force constant frame rate so playback is smooth (capture frames arrive irregularly → bogus
        // r_frame_rate 300 that players judder on), and tag BT.709 limited-range so colors aren't
        // washed out (desktop capture is full-range RGB; untagged YUV gets mis-interpreted).
        string pixFmt = nv12 ? "" : "-pix_fmt yuv420p ";
        sb.Append($"-c:v h264_nvenc -preset {_cfg.NvencPreset} -rc vbr -cq {_cfg.Cq} {pixFmt}-g {_cfg.Fps} -r {_cfg.Fps} -vsync cfr ");
        sb.Append("-colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv ");
        if (!string.IsNullOrWhiteSpace(extraEncoderArgs)) sb.Append(extraEncoderArgs).Append(' ');
        sb.Append("-c:a aac -b:a 160k ");
        // MP4/TS keep handler_name; the app DB holds the authoritative track map.
        for (int i = 0; i < _audio.Count; i++)
            sb.Append($"-metadata:s:a:{i} handler_name=\"{_audio[i].cfg.Name}\" ");
        sb.Append(sinkArgs);

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
                lock (_ffmpegErr) { _ffmpegErr.AppendLine(line); if (_ffmpegErr.Length > 16000) _ffmpegErr.Remove(0, _ffmpegErr.Length - 16000); }
        });
        return proc;
    }

    private void OnFrame(byte[] buf) { lock (_frameLock) _latestFrame = buf; }

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

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _videoThread?.Join(2000);
        try { _video?.Stop(); } catch { }
        foreach (var a in _audio) { try { a.cap.Stop(); } catch { } }
        try { _videoPipe?.Dispose(); } catch { }
        foreach (var a in _audio) { try { a.pipe.Dispose(); } catch { } }
        if (_ffmpeg is not null && !_ffmpeg.WaitForExit(15000))
            try { _ffmpeg.Kill(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        _video?.Dispose();
        foreach (var a in _audio) a.cap.Dispose();
    }
}
