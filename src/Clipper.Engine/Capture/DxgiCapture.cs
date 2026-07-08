using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using D3D11Format = Vortice.DXGI.Format;

namespace Clipper.Engine;

/// <summary>
/// Screen capture via DXGI Desktop Duplication (IDXGIOutputDuplication). Unlike WGC this
/// does not depend on the per-user CaptureService, so it works on debloated machines.
/// Captures the whole primary output. Throwaway spike code.
/// </summary>
sealed class DxgiCapture : ICapture
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public CaptureFormat Format => CaptureFormat.Bgra;
    public event Action<byte[]>? FrameBgra;

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private IDXGIOutputDuplication _dup = null!;
    private ID3D11Texture2D? _staging;
    private Thread? _thread;
    private volatile bool _running;

    public void Start()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out IDXGIAdapter1 adapter).CheckError();
        using (adapter)
        {
            // DriverType must be Unknown when an explicit adapter is supplied.
            D3D11.D3D11CreateDevice(
                adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                out ID3D11Device? device).CheckError();
            _device = device!;
            _context = _device.ImmediateContext;

            adapter.EnumOutputs(0, out IDXGIOutput output).CheckError();
            using (output)
            using (var output1 = output.QueryInterface<IDXGIOutput1>())
            {
                _dup = output1.DuplicateOutput(_device);
            }
        }

        var mode = _dup.Description.ModeDescription;
        Width = (int)mode.Width;
        Height = (int)mode.Height;

        _running = true;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "DxgiCapture" };
        _thread.Start();
    }

    private void CaptureLoop()
    {
        while (_running)
        {
            var result = _dup.AcquireNextFrame(500, out _, out IDXGIResource? resource);
            if (result == Vortice.DXGI.ResultCode.WaitTimeout)
                continue;
            if (result.Failure)
                break; // e.g. access lost on desktop switch — spike just stops

            try
            {
                using var tex = resource!.QueryInterface<ID3D11Texture2D>();
                EnsureStaging(tex.Description);
                _context.CopyResource(_staging!, tex);

                MappedSubresource map = _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
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
                finally { _context.Unmap(_staging!, 0); }
            }
            finally
            {
                resource?.Dispose();
                _dup.ReleaseFrame();
            }
        }
    }

    private void EnsureStaging(Texture2DDescription desc)
    {
        if (_staging is not null && _staging.Description.Width == desc.Width && _staging.Description.Height == desc.Height)
            return;
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

    public void Stop()
    {
        _running = false;
        _thread?.Join(1000);
        try { _dup?.Dispose(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        _staging?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
    }
}
