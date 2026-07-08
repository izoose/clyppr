using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Clipper.Engine;

/// <summary>
/// Hardware BGRA→NV12 conversion via the D3D11 video processor. Converting on the GPU (instead of
/// piping raw BGRA and letting ffmpeg do a CPU swscale) cuts the pipe bandwidth by ~2.6x (12bpp vs
/// 32bpp) and removes the CPU colour conversion entirely. Output is BT.709 limited range so it
/// matches the tags ffmpeg writes. Reused across frames; not thread-safe (call from one thread).
/// </summary>
sealed class Nv12Converter : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly int _w, _h;

    private ID3D11VideoDevice _videoDevice = null!;
    private ID3D11VideoContext _videoContext = null!;
    private ID3D11VideoProcessorEnumerator _enum = null!;
    private ID3D11VideoProcessor _proc = null!;
    private ID3D11Texture2D _rgbInput = null!;   // stable BGRA input (copy target for each frame)
    private ID3D11Texture2D _nv12 = null!;        // GPU NV12 output
    private ID3D11Texture2D _nv12Staging = null!; // CPU-readable NV12
    private ID3D11VideoProcessorInputView _inputView = null!;
    private ID3D11VideoProcessorOutputView _outputView = null!;

    public Nv12Converter(ID3D11Device device, ID3D11DeviceContext context, int width, int height, int fps)
    {
        _device = device;
        _context = context;
        _w = width;
        _h = height;
        uint uw = (uint)width, uh = (uint)height;

        _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
        _videoContext = context.QueryInterface<ID3D11VideoContext>();

        var content = new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputWidth = uw,
            InputHeight = uh,
            OutputWidth = uw,
            OutputHeight = uh,
            InputFrameRate = new Rational((uint)fps, 1),
            OutputFrameRate = new Rational((uint)fps, 1),
            Usage = VideoUsage.PlaybackNormal,
        };
        _enum = _videoDevice.CreateVideoProcessorEnumerator(content);
        _proc = _videoDevice.CreateVideoProcessor(_enum, 0);

        _rgbInput = device.CreateTexture2D(new Texture2DDescription
        {
            Width = uw,
            Height = uh,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        });

        _nv12 = device.CreateTexture2D(new Texture2DDescription
        {
            Width = uw,
            Height = uh,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.NV12,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        });

        _nv12Staging = device.CreateTexture2D(new Texture2DDescription
        {
            Width = uw,
            Height = uh,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.NV12,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        });

        _inputView = _videoDevice.CreateVideoProcessorInputView(_rgbInput, _enum,
            new VideoProcessorInputViewDescription
            {
                FourCC = 0,
                ViewDimension = VideoProcessorInputViewDimension.Texture2D,
                Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 },
            });

        _outputView = _videoDevice.CreateVideoProcessorOutputView(_nv12, _enum,
            new VideoProcessorOutputViewDescription
            {
                ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
                Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 },
            });

        // Input = full-range RGB (desktop); output = BT.709, studio/limited (16-235) YUV.
        // Nominal_Range: 1 = 16-235 (studio), 2 = 0-255 (full). YCbCr_Matrix: 1 = BT.709.
        _videoContext.VideoProcessorSetStreamColorSpace(_proc, 0,
            new VideoProcessorColorSpace { RGB_Range = 0, Nominal_Range = 2 });
        _videoContext.VideoProcessorSetOutputColorSpace(_proc,
            new VideoProcessorColorSpace { YCbCr_Matrix = 1, Nominal_Range = 1 });
    }

    /// <summary>Converts a BGRA texture to a tightly-packed NV12 byte buffer (Y plane then interleaved UV).</summary>
    public byte[] Convert(ID3D11Texture2D bgraSource)
    {
        _context.CopyResource(_rgbInput, bgraSource);

        var stream = new VideoProcessorStream
        {
            Enable = true,
            InputSurface = _inputView,
        };
        _videoContext.VideoProcessorBlt(_proc, _outputView, 0, 1, new[] { stream });

        _context.CopyResource(_nv12Staging, _nv12);

        MappedSubresource map = _context.Map(_nv12Staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int w = _w, h = _h;
            int pitch = (int)map.RowPitch;
            var buffer = new byte[w * h + w * h / 2];
            unsafe
            {
                byte* src = (byte*)map.DataPointer;
                fixed (byte* dst = buffer)
                {
                    // Y plane: h rows of w bytes.
                    for (int y = 0; y < h; y++)
                        Buffer.MemoryCopy(src + (long)y * pitch, dst + (long)y * w, w, w);
                    // UV plane: starts at pitch*h in the mapped data; h/2 rows of w bytes.
                    byte* uvSrc = src + (long)pitch * h;
                    byte* uvDst = dst + (long)w * h;
                    for (int y = 0; y < h / 2; y++)
                        Buffer.MemoryCopy(uvSrc + (long)y * pitch, uvDst + (long)y * w, w, w);
                }
            }
            return buffer;
        }
        finally
        {
            _context.Unmap(_nv12Staging, 0);
        }
    }

    public void Dispose()
    {
        _inputView?.Dispose();
        _outputView?.Dispose();
        _rgbInput?.Dispose();
        _nv12?.Dispose();
        _nv12Staging?.Dispose();
        _proc?.Dispose();
        _enum?.Dispose();
        _videoContext?.Dispose();
        _videoDevice?.Dispose();
    }
}
