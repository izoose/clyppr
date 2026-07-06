using System.Runtime.InteropServices;
using static Clipper.Engine.Native;

namespace Clipper.Engine;

enum LoopbackMode { IncludeTree, ExcludeTree }

/// <summary>What a track captures.</summary>
public enum CaptureKind
{
    /// <summary>Only the target process (and its child processes).</summary>
    ProcessInclude,
    /// <summary>Everything except the target process tree (e.g. "game minus Discord").</summary>
    ProcessExclude,
    /// <summary>The default microphone / capture endpoint.</summary>
    Mic,
    /// <summary>All system render audio (default speakers loopback).</summary>
    SystemAudio,
}

public readonly record struct AudioFormat(int SampleRate, int Channels, int BitsPerSample)
{
    public int BlockAlign => Channels * BitsPerSample / 8;
}

/// <summary>
/// Captures one audio source (process include/exclude, mic, or full system) via WASAPI and
/// raises <see cref="DataAvailable"/> with continuous PCM in <see cref="Format"/>. Emits
/// silence when the source is silent so downstream muxing stays time-aligned.
/// </summary>
public sealed class AudioCapture : IDisposable
{
    // Fixed pipeline format. Process-loopback clients can't GetMixFormat; real devices are
    // converted to this via AUTOCONVERTPCM.
    public static readonly AudioFormat Format = new(48000, 2, 16);

    public event Action<byte[]>? DataAvailable;

    private readonly CaptureKind _kind;
    private readonly uint _pid;
    private IAudioClient _client = null!;
    private IAudioCaptureClient _capture = null!;
    private IntPtr _hEvent;
    private IntPtr _pProp, _pParams, _pFormat;
    private Thread? _thread;
    private volatile bool _running;

    public AudioCapture(CaptureKind kind, uint pid = 0)
    {
        _kind = kind;
        _pid = pid;
    }

    public void Start()
    {
        _client = _kind is CaptureKind.ProcessInclude or CaptureKind.ProcessExclude
            ? ActivateProcessLoopback()
            : ActivateDefaultEndpoint();

        // Stream flags per source kind.
        uint flags = AUDCLNT_STREAMFLAGS_EVENTCALLBACK;
        if (_kind is CaptureKind.ProcessInclude or CaptureKind.ProcessExclude or CaptureKind.SystemAudio)
            flags |= AUDCLNT_STREAMFLAGS_LOOPBACK;
        if (_kind is CaptureKind.Mic or CaptureKind.SystemAudio)
            flags |= AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;

        var wfx = new WAVEFORMATEX
        {
            wFormatTag = WAVE_FORMAT_PCM,
            nChannels = (ushort)Format.Channels,
            nSamplesPerSec = (uint)Format.SampleRate,
            wBitsPerSample = (ushort)Format.BitsPerSample,
            nBlockAlign = (ushort)Format.BlockAlign,
            nAvgBytesPerSec = (uint)(Format.SampleRate * Format.BlockAlign),
            cbSize = 0,
        };
        _pFormat = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEFORMATEX>());
        Marshal.StructureToPtr(wfx, _pFormat, false);

        const long hns200ms = 2_000_000;
        Marshal.ThrowExceptionForHR(_client.Initialize(
            AUDCLNT_SHAREMODE_SHARED, flags, hns200ms, 0, _pFormat, IntPtr.Zero));

        _hEvent = CreateEventW(IntPtr.Zero, false, false, null);
        Marshal.ThrowExceptionForHR(_client.SetEventHandle(_hEvent));

        Marshal.ThrowExceptionForHR(_client.GetService(IID_IAudioCaptureClient, out IntPtr pCap));
        _capture = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(pCap);
        Marshal.Release(pCap);

        Marshal.ThrowExceptionForHR(_client.Start());

        _running = true;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = $"audio-{_kind}" };
        _thread.Start();
    }

    private IAudioClient ActivateProcessLoopback()
    {
        var actParams = new AUDIOCLIENT_ACTIVATION_PARAMS
        {
            ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK,
            TargetProcessId = (int)_pid,
            ProcessLoopbackMode = _kind == CaptureKind.ProcessInclude
                ? PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE
                : PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE,
        };
        _pParams = Marshal.AllocHGlobal(Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>());
        Marshal.StructureToPtr(actParams, _pParams, false);

        var prop = new PROPVARIANT_BLOB
        {
            vt = VT_BLOB,
            cbSize = (uint)Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>(),
            pBlobData = _pParams,
        };
        _pProp = Marshal.AllocHGlobal(Marshal.SizeOf<PROPVARIANT_BLOB>());
        Marshal.StructureToPtr(prop, _pProp, false);

        var handler = new ActivationHandler();
        ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, IID_IAudioClient, _pProp, handler, out var op);
        if (!handler.Done.WaitOne(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("ActivateAudioInterfaceAsync did not complete.");
        Marshal.ThrowExceptionForHR(op.GetActivateResult(out int activateHr, out object clientObj));
        Marshal.ThrowExceptionForHR(activateHr);
        return (IAudioClient)clientObj;
    }

    private IAudioClient ActivateDefaultEndpoint()
    {
        var enumr = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator)!)!;
        int flow = _kind == CaptureKind.Mic ? EDataFlow_eCapture : EDataFlow_eRender;
        Marshal.ThrowExceptionForHR(enumr.GetDefaultAudioEndpoint(flow, ERole_eConsole, out IMMDevice dev));
        Marshal.ThrowExceptionForHR(dev.Activate(IID_IAudioClient, CLSCTX_ALL, IntPtr.Zero, out object clientObj));
        return (IAudioClient)clientObj;
    }

    private void CaptureLoop()
    {
        int blockAlign = Format.BlockAlign;
        while (_running)
        {
            WaitForSingleObject(_hEvent, 100);
            while (_capture.GetNextPacketSize(out uint packetFrames) == 0 && packetFrames > 0)
            {
                int gb = _capture.GetBuffer(out IntPtr pData, out uint frames, out uint bufFlags, out _, out _);
                if (gb != 0 || frames == 0) { if (frames > 0) _capture.ReleaseBuffer(frames); break; }

                int bytes = (int)frames * blockAlign;
                var buffer = new byte[bytes];
                if ((bufFlags & AUDCLNT_BUFFERFLAGS_SILENT) == 0)
                    Marshal.Copy(pData, buffer, 0, bytes);
                _capture.ReleaseBuffer(frames);

                DataAvailable?.Invoke(buffer);
            }
        }
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(1000);
        try { _client?.Stop(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        if (_hEvent != IntPtr.Zero) { CloseHandle(_hEvent); _hEvent = IntPtr.Zero; }
        if (_pProp != IntPtr.Zero) { Marshal.FreeHGlobal(_pProp); _pProp = IntPtr.Zero; }
        if (_pParams != IntPtr.Zero) { Marshal.FreeHGlobal(_pParams); _pParams = IntPtr.Zero; }
        if (_pFormat != IntPtr.Zero) { Marshal.FreeHGlobal(_pFormat); _pFormat = IntPtr.Zero; }
    }

    private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler
    {
        public readonly ManualResetEvent Done = new(false);
        public int ActivateCompleted(IntPtr activateOperation) { Done.Set(); return 0; }
    }
}
