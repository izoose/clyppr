namespace Clipper.Engine;

/// <summary>
/// Manual recorder: Start begins recording the display + configured audio tracks to a single MP4;
/// Stop finalizes it and returns the path.
/// </summary>
public sealed class Recorder : IDisposable
{
    private readonly RecorderConfig _cfg;
    private readonly CaptureSession _session;

    public bool IsRecording => _session.IsRunning;
    public string? CurrentFile { get; private set; }
    public IReadOnlyList<string> TrackNames => _session.TrackNames;
    public string FfmpegLog => _session.FfmpegLog;

    public Recorder(RecorderConfig cfg)
    {
        _cfg = cfg;
        _session = new CaptureSession(cfg);
    }

    public void Start()
    {
        Directory.CreateDirectory(_cfg.OutputDirectory);
        CurrentFile = Path.Combine(_cfg.OutputDirectory, $"clip_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        _session.Start($"-movflags +faststart \"{CurrentFile}\"");
    }

    public string Stop()
    {
        _session.Stop();
        return CurrentFile ?? "";
    }

    public void Dispose() => _session.Dispose();
}
