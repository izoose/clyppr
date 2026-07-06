using System.Text;

/// <summary>Minimal RIFF/WAVE PCM writer. Back-patches the two size fields on Dispose.</summary>
sealed class WavWriter : IDisposable
{
    private readonly FileStream _fs;
    private readonly BinaryWriter _bw;
    private readonly object _lock = new();
    private int _dataBytes;

    public WavWriter(string path, int sampleRate, int channels, int bitsPerSample)
    {
        _fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        _bw = new BinaryWriter(_fs);
        int blockAlign = channels * bitsPerSample / 8;

        _bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        _bw.Write(0);                         // RIFF chunk size — patched later
        _bw.Write(Encoding.ASCII.GetBytes("WAVE"));

        _bw.Write(Encoding.ASCII.GetBytes("fmt "));
        _bw.Write(16);                        // fmt chunk size
        _bw.Write((short)1);                  // PCM
        _bw.Write((short)channels);
        _bw.Write(sampleRate);
        _bw.Write(sampleRate * blockAlign);   // byte rate
        _bw.Write((short)blockAlign);
        _bw.Write((short)bitsPerSample);

        _bw.Write(Encoding.ASCII.GetBytes("data"));
        _bw.Write(0);                         // data chunk size — patched later
    }

    public void Write(byte[] pcm)
    {
        lock (_lock)
        {
            _bw.Write(pcm);
            _dataBytes += pcm.Length;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _bw.Flush();
            _fs.Seek(4, SeekOrigin.Begin);
            _bw.Write(36 + _dataBytes);       // RIFF size = 36 + data
            _fs.Seek(40, SeekOrigin.Begin);
            _bw.Write(_dataBytes);            // data size
            _bw.Flush();
            _bw.Dispose();
        }
    }
}
