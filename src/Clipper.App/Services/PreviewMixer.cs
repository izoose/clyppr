using System.Diagnostics;
using System.IO;
using System.Text;
using Clipper.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Clipper.App;

/// <summary>
/// Real-time preview of the multi-track clip audio. Each app's track is decoded to its
/// own stream and summed through a mixer with an independent per-track gain, so moving a
/// volume slider (or muting a source) is heard immediately — no re-export. The video plays
/// muted in the MediaElement; this owns all audible sound during editing.
/// </summary>
public sealed class PreviewMixer : IDisposable
{
    private readonly string _tmpDir;
    private readonly List<AudioFileReader> _readers = new();
    private readonly List<VolumeSampleProvider> _gains = new();
    private VolumeSampleProvider? _master;
    private WaveOutEvent? _out;
    private bool _disposed;

    public int TrackCount => _readers.Count;
    public TimeSpan Position => _readers.Count > 0 ? _readers[0].CurrentTime : TimeSpan.Zero;

    private PreviewMixer(string tmpDir) => _tmpDir = tmpDir;

    /// <summary>Extracts each audio track to a temp WAV and wires up the mixer. Returns null if
    /// the clip has no audio or extraction fails (editor then just previews video-only).</summary>
    public static async Task<PreviewMixer?> CreateAsync(string filePath, int trackCount, string ffmpegPath = "ffmpeg")
    {
        if (trackCount <= 0) return null;
        // The clip may hold fewer real audio streams than the DB lists track names for
        // (e.g. an already-exported clip mixed down to one). Only map streams that exist.
        int actual = FfProbe.Read(filePath)?.AudioTracks.Count ?? trackCount;
        trackCount = Math.Min(trackCount, actual);
        if (trackCount <= 0) return null;

        string tmp = Path.Combine(Path.GetTempPath(), "clipper_preview_" + Guid.NewGuid().ToString("N"));
        var mixer = new PreviewMixer(tmp);
        try
        {
            Directory.CreateDirectory(tmp);
            var wavs = new List<string>();
            var args = new StringBuilder($"-y -hide_banner -loglevel error -i \"{filePath}\"");
            for (int i = 0; i < trackCount; i++)
            {
                string w = Path.Combine(tmp, $"t{i}.wav");
                args.Append($" -map 0:a:{i} -ac 2 -ar 48000 -c:a pcm_s16le \"{w}\"");
                wavs.Add(w);
            }
            await Task.Run(() => RunFfmpeg(ffmpegPath, args.ToString()));

            var sources = new List<ISampleProvider>();
            foreach (var w in wavs)
            {
                if (!File.Exists(w)) continue;
                var reader = new AudioFileReader(w);
                var gain = new VolumeSampleProvider(reader) { Volume = 1f };
                mixer._readers.Add(reader);
                mixer._gains.Add(gain);
                sources.Add(gain);
            }
            if (sources.Count == 0) { mixer.Dispose(); return null; }

            var mix = new MixingSampleProvider(sources) { ReadFully = true };
            mixer._master = new VolumeSampleProvider(mix) { Volume = 1f };
            mixer._out = new WaveOutEvent { DesiredLatency = 120 };
            mixer._out.Init(mixer._master);
            return mixer;
        }
        catch
        {
            mixer.Dispose();
            return null;
        }
    }

    public void SetVolume(int track, double volume)
    {
        if (track >= 0 && track < _gains.Count) _gains[track].Volume = (float)Math.Clamp(volume, 0, 4);
    }

    /// <summary>Master preview level — used by the editor's mute button (0 = silent, 1 = full).</summary>
    public void SetMasterVolume(float v)
    {
        if (_master is not null) _master.Volume = Math.Clamp(v, 0, 1);
    }

    public void Play() { if (!_disposed) _out?.Play(); }
    public void Pause() { if (!_disposed) _out?.Pause(); }

    public void Seek(TimeSpan t)
    {
        foreach (var r in _readers)
        {
            var clamped = t < TimeSpan.Zero ? TimeSpan.Zero : (t > r.TotalTime ? r.TotalTime : t);
            try { r.CurrentTime = clamped; } catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _out?.Stop(); _out?.Dispose(); } catch { }
        foreach (var r in _readers) { try { r.Dispose(); } catch { } }
        _readers.Clear();
        _gains.Clear();
        try { if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, recursive: true); } catch { }
    }

    private static void RunFfmpeg(string exe, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("ffmpeg extract failed: " + err);
    }
}
