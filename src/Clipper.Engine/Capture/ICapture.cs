namespace Clipper.Engine;

/// <summary>Pixel layout of the frames an <see cref="ICapture"/> delivers.</summary>
enum CaptureFormat
{
    /// <summary>Tightly-packed 32bpp BGRA (4 bytes/pixel).</summary>
    Bgra,
    /// <summary>Tightly-packed NV12 (Y plane then interleaved UV, 12bpp), GPU-converted to BT.709 limited range.</summary>
    Nv12,
}

/// <summary>
/// Common shape for a screen-capture backend. Frames are delivered tightly-packed (row padding
/// removed) via <see cref="FrameBgra"/>; <see cref="Format"/> says how to interpret the bytes.
/// Two implementations: WgcCapture (Windows Graphics Capture) and DxgiCapture (Desktop
/// Duplication). The engine tries WGC first, falls back to DXGI.
/// </summary>
interface ICapture : IDisposable
{
    int Width { get; }
    int Height { get; }
    /// <summary>Pixel format of the delivered frames.</summary>
    CaptureFormat Format { get; }
    event Action<byte[]> FrameBgra;
    void Start();
    void Stop();
}
