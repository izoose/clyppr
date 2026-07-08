using System.IO;
using System.Windows;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Clipper.App;

/// <summary>Plays the short "clip saved" chime at a user-set volume (0 = off .. 1 = full).</summary>
public static class Sounds
{
    private static byte[]? _clipWav;
    private static WaveOutEvent? _out;

    public static void PlayClip(float volume)
    {
        if (volume <= 0.001f) return;
        try
        {
            _clipWav ??= LoadResource();
            if (_clipWav is null) return;

            var reader = new WaveFileReader(new MemoryStream(_clipWav));
            var gain = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = Math.Clamp(volume, 0f, 1.5f) };

            try { _out?.Dispose(); } catch { }
            _out = new WaveOutEvent();
            _out.PlaybackStopped += (_, _) => { try { reader.Dispose(); } catch { } };
            _out.Init(gain);
            _out.Play();
        }
        catch { /* never let a sound crash anything */ }
    }

    private static byte[]? LoadResource()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/clip.wav"));
            if (info?.Stream is not Stream s) return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }
}
