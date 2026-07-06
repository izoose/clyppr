using System.Runtime.InteropServices;
using static Native;

enum LoopbackMode { IncludeTree, ExcludeTree }

readonly record struct AudioFormat(int SampleRate, int Channels, int BitsPerSample)
{
    public int BlockAlign => Channels * BitsPerSample / 8;
}

/// <summary>
/// Captures one process (and its tree) INCLUDED or EXCLUDED via WASAPI process-loopback,
/// raising DataAvailable with raw PCM in <see cref="Format"/>. Throwaway spike code.
/// </summary>
sealed class ProcessLoopbackCapture : IDisposable
{
    // Fixed capture format — process-loopback clients don't support GetMixFormat, so we ask for this.
    public static readonly AudioFormat Format = new(48000, 2, 16);

    public event Action<byte[]>? DataAvailable;

    private readonly uint _pid;
    private readonly LoopbackMode _mode;
    private IAudioClient _client = null!;
    private IAudioCaptureClient _capture = null!;
    private IntPtr _hEvent;
    private IntPtr _pProp, _pParams, _pFormat;
    private Thread? _thread;
    private volatile bool _running;

    public ProcessLoopbackCapture(uint pid, LoopbackMode mode)
    {
        _pid = pid;
        _mode = mode;
    }

    public void Start()
    {
        // 1. Build PROPVARIANT(VT_BLOB -> AUDIOCLIENT_ACTIVATION_PARAMS).
        var actParams = new AUDIOCLIENT_ACTIVATION_PARAMS
        {
            ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK,
            TargetProcessId = (int)_pid,
            ProcessLoopbackMode = _mode == LoopbackMode.IncludeTree
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

        // 2. Activate the process-loopback audio interface (async — wait for completion).
        var handler = new ActivationHandler();
        ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, IID_IAudioClient, _pProp, handler, out var op);
        if (!handler.Done.WaitOne(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("ActivateAudioInterfaceAsync did not complete.");
        int hr = op.GetActivateResult(out int activateHr, out object clientObj);
        Marshal.ThrowExceptionForHR(hr);
        Marshal.ThrowExceptionForHR(activateHr);
        _client = (IAudioClient)clientObj;

        // 3. Initialize in shared loopback + event-driven mode with our fixed PCM format.
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
            AUDCLNT_SHAREMODE_SHARED,
            AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
            hns200ms, 0, _pFormat, IntPtr.Zero));

        _hEvent = CreateEventW(IntPtr.Zero, false, false, null);
        Marshal.ThrowExceptionForHR(_client.SetEventHandle(_hEvent));

        Marshal.ThrowExceptionForHR(_client.GetService(IID_IAudioCaptureClient, out IntPtr pCap));
        _capture = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(pCap);
        Marshal.Release(pCap);

        Marshal.ThrowExceptionForHR(_client.Start());

        _running = true;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = $"loopback-{_mode}" };
        _thread.Start();
    }

    private void CaptureLoop()
    {
        int blockAlign = Format.BlockAlign;
        while (_running)
        {
            WaitForSingleObject(_hEvent, 100);
            // Drain all queued packets (works whether or not the event fired).
            while (_capture.GetNextPacketSize(out uint packetFrames) == 0 && packetFrames > 0)
            {
                int gb = _capture.GetBuffer(out IntPtr pData, out uint frames, out uint flags, out _, out _);
                if (gb != 0 || frames == 0) { if (frames > 0) _capture.ReleaseBuffer(frames); break; }

                int bytes = (int)frames * blockAlign;
                var buffer = new byte[bytes];
                if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) == 0)
                    Marshal.Copy(pData, buffer, 0, bytes);   // else leave zeros (silence)
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
