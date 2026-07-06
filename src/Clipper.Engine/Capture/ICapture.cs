namespace Clipper.Engine;

/// <summary>
/// Common shape for a screen-capture backend that delivers tightly-packed BGRA frames.
/// Two implementations: WgcCapture (Windows Graphics Capture) and DxgiCapture
/// (Desktop Duplication). The engine tries WGC first, falls back to DXGI.
/// </summary>
interface ICapture : IDisposable
{
    int Width { get; }
    int Height { get; }
    event Action<byte[]> FrameBgra;
    void Start();
    void Stop();
}
