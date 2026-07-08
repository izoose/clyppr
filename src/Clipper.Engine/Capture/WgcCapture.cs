using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using D3D11Format = Vortice.DXGI.Format;

namespace Clipper.Engine;

/// <summary>
/// Windows Graphics Capture of the primary monitor. Delivers each frame via FrameBgra.
/// When <c>preferNv12</c> is requested (and the GPU supports it), frames are converted to NV12
/// on the GPU (see <see cref="Nv12Converter"/>) — halving the pipe bandwidth and removing ffmpeg's
/// CPU colour conversion; otherwise a plain BGRA readback is used. <see cref="Format"/> reflects
/// which one is active (decided during Start, before frames flow, so the recorder can configure ffmpeg).
/// </summary>
sealed class WgcCapture : ICapture
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public CaptureFormat Format { get; private set; } = CaptureFormat.Bgra;
    public event Action<byte[]>? FrameBgra;

    private readonly int _fps;
    private bool _preferNv12;
    private Nv12Converter? _nv12Conv;
    private int _nv12W, _nv12H;

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private ID3D11Texture2D? _staging;
    private Direct3D11CaptureFramePool _framePool = null!;
    private GraphicsCaptureSession _session = null!;
    private GraphicsCaptureItem _item = null!;

    public WgcCapture(bool preferNv12 = false, int fps = 60)
    {
        _preferNv12 = preferNv12;
        _fps = fps;
    }

    public void Start()
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new NotSupportedException("Windows Graphics Capture is not supported on this OS.");

        // 1. D3D11 device (BGRA support required for WGC).
        D3D11.D3D11CreateDevice(
            null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
            out ID3D11Device? device).CheckError();
        _device = device!;
        _context = _device.ImmediateContext;

        // 2. Wrap the D3D11 device as a WinRT IDirect3DDevice for WGC.
        var winrtDevice = CreateWinrtDevice(_device);

        // 3. Build a GraphicsCaptureItem for the primary monitor.
        IntPtr hmon = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
        _item = CreateItemForMonitor(hmon);

        SizeInt32 size = _item.Size;
        Width = size.Width;
        Height = size.Height;

        // Decide the output format up front so the recorder configures ffmpeg correctly.
        if (_preferNv12) EnsureNv12(Width, Height);

        // 4. Free-threaded frame pool → FrameArrived on a threadpool thread (no dispatcher needed).
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
        _framePool.FrameArrived += OnFrameArrived;

        _session = _framePool.CreateCaptureSession(_item);
        try { _session.IsBorderRequired = false; } catch { /* needs capability; fine for spike */ }
        _session.StartCapture();
    }

    /// <summary>Creates (or resizes) the GPU NV12 converter; falls back to BGRA on any failure.</summary>
    private void EnsureNv12(int w, int h)
    {
        if (!_preferNv12) return;
        if (w % 2 != 0 || h % 2 != 0) { DisableNv12(); return; } // NV12 needs even dimensions
        if (_nv12Conv is not null && _nv12W == w && _nv12H == h) return;
        try
        {
            _nv12Conv?.Dispose();
            _nv12Conv = new Nv12Converter(_device, _context, w, h, _fps);
            _nv12W = w; _nv12H = h;
            Width = w; Height = h;
            Format = CaptureFormat.Nv12;
        }
        catch
        {
            DisableNv12();
        }
    }

    private void DisableNv12()
    {
        _nv12Conv?.Dispose();
        _nv12Conv = null;
        _preferNv12 = false;
        Format = CaptureFormat.Bgra;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object args)
    {
        using Direct3D11CaptureFrame? frame = pool.TryGetNextFrame();
        if (frame is null) return;

        using ID3D11Texture2D srcTex = GetTexture(frame.Surface);
        Texture2DDescription desc = srcTex.Description;

        // GPU NV12 path.
        if (_preferNv12)
        {
            EnsureNv12((int)desc.Width, (int)desc.Height);
            if (_nv12Conv is not null)
            {
                try
                {
                    FrameBgra?.Invoke(_nv12Conv.Convert(srcTex));
                    return;
                }
                catch
                {
                    DisableNv12(); // fall through to BGRA
                }
            }
        }

        // BGRA readback path.
        if (_staging is null || _staging.Description.Width != desc.Width || _staging.Description.Height != desc.Height)
        {
            _staging?.Dispose();
            _staging = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = desc.Width,
                Height = desc.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = D3D11Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
                MiscFlags = ResourceOptionFlags.None,
            });
            Width = (int)desc.Width;
            Height = (int)desc.Height;
        }

        _context.CopyResource(_staging, srcTex);

        MappedSubresource map = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int w = Width, h = Height;
            var buffer = new byte[w * h * 4];
            unsafe
            {
                byte* src = (byte*)map.DataPointer;
                fixed (byte* dst = buffer)
                {
                    for (int y = 0; y < h; y++)
                        Buffer.MemoryCopy(src + (long)y * map.RowPitch, dst + (long)y * w * 4, w * 4, w * 4);
                }
            }
            FrameBgra?.Invoke(buffer);
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }
    }

    public void Stop()
    {
        try { _session?.Dispose(); } catch { }
        try { _framePool?.Dispose(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        _nv12Conv?.Dispose();
        _staging?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
    }

    // ---- interop helpers ----

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        Guid iid = typeof(ID3D11Texture2D).GUID;
        IntPtr ptr = access.GetInterface(ref iid);
        return new ID3D11Texture2D(ptr);
    }

    static GraphicsCaptureItem CreateItemForMonitor(IntPtr hmon)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        Guid iid = GraphicsCaptureItemIid;
        IntPtr itemPtr = interop.CreateForMonitor(hmon, ref iid);
        var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
        Marshal.Release(itemPtr);
        return item;
    }

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true)]
    static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    static IDirect3DDevice CreateWinrtDevice(ID3D11Device d3dDevice)
    {
        using var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr pUnknown));
        var winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown);
        Marshal.Release(pUnknown);
        return winrtDevice;
    }

    const uint MONITOR_DEFAULTTOPRIMARY = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
}
