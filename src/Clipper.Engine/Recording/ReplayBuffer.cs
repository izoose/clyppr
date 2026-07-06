using System.Diagnostics;

namespace Clipper.Engine;

/// <summary>
/// Always-on replay buffer. Continuously encodes the display + audio tracks into rotating,
/// keyframe-aligned 1-second MPEG-TS segments on disk; a janitor keeps only the last
/// <c>maxBufferSeconds</c>. <see cref="SaveLast"/> concatenates the newest segments into an MP4 —
/// this is the "press ALT+C to save the last minute" clip.
/// </summary>
public sealed class ReplayBuffer : IDisposable
{
    private readonly RecorderConfig _cfg;
    private readonly int _maxSeconds;
    private readonly CaptureSession _session;
    private string _segDir = "";
    private Thread? _janitor;
    private volatile bool _running;
    private int _saveGuard;

    public bool IsRunning => _session.IsRunning;
    public IReadOnlyList<string> TrackNames => _session.TrackNames;
    public string FfmpegLog => _session.FfmpegLog;

    public ReplayBuffer(RecorderConfig cfg, int maxBufferSeconds = 300)
    {
        _cfg = cfg;
        _maxSeconds = maxBufferSeconds;
        _session = new CaptureSession(cfg);
    }

    public void Start()
    {
        _segDir = Path.GetFullPath(Path.Combine(_cfg.OutputDirectory, ".buffer"));
        if (Directory.Exists(_segDir)) { try { Directory.Delete(_segDir, true); } catch { } }
        Directory.CreateDirectory(_segDir);

        string segPattern = Path.Combine(_segDir, "seg_%06d.ts");
        string sink = $"-f segment -segment_time 1 -segment_format mpegts -reset_timestamps 1 \"{segPattern}\"";
        _session.Start(sink, "-force_key_frames expr:gte(t,n_forced*1)");

        _running = true;
        _janitor = new Thread(Janitor) { IsBackground = true, Name = "buffer-janitor" };
        _janitor.Start();
    }

    private void Janitor()
    {
        while (_running)
        {
            try
            {
                var segs = SegFiles();
                int keep = _maxSeconds + 3;
                if (segs.Count > keep)
                    foreach (var f in segs.Take(segs.Count - keep))
                        try { File.Delete(f); } catch { }
            }
            catch { }
            Thread.Sleep(2000);
        }
    }

    // Zero-padded names sort lexically == chronologically.
    private List<string> SegFiles() =>
        Directory.EnumerateFiles(_segDir, "seg_*.ts").OrderBy(f => f, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Save roughly the last <paramref name="seconds"/> seconds of the buffer to <paramref name="outPath"/>.
    /// Returns the path, or null if a save is already in progress or the buffer isn't ready.
    /// </summary>
    public string? SaveLast(int seconds, string outPath)
    {
        if (Interlocked.Exchange(ref _saveGuard, 1) == 1) return null;
        try
        {
            var segs = SegFiles();
            if (segs.Count < 2) return null;

            // Skip the newest segment (may be mid-write); take the N before it.
            var usable = segs.Take(segs.Count - 1).ToList();
            int take = Math.Min(seconds, usable.Count);
            var chosen = usable.Skip(usable.Count - take).ToList();

            string listPath = Path.Combine(_segDir, $"concat_{Guid.NewGuid():N}.txt");
            File.WriteAllLines(listPath, chosen.Select(f => $"file '{f.Replace('\\', '/')}'"));

            var psi = new ProcessStartInfo
            {
                FileName = _cfg.FfmpegPath,
                // -map 0 keeps ALL streams (every audio track); default selection would keep only one.
                Arguments = $"-y -hide_banner -loglevel error -f concat -safe 0 -i \"{listPath}\" -map 0 -c copy -movflags +faststart \"{outPath}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            try { File.Delete(listPath); } catch { }
            if (p.ExitCode != 0) throw new IOException("Replay concat failed: " + err);
            return outPath;
        }
        finally { Interlocked.Exchange(ref _saveGuard, 0); }
    }

    public void Stop()
    {
        _running = false;
        _janitor?.Join(2500);
        _session.Stop();
    }

    public void Dispose()
    {
        Stop();
        _session.Dispose();
        try { Directory.Delete(_segDir, true); } catch { }
    }
}
