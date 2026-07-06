using System.Runtime.InteropServices;

namespace Clipper.Engine;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>
/// A system-wide hotkey. Runs a dedicated message-loop thread and registers the hotkey with
/// hWnd = NULL, so WM_HOTKEY is posted to that thread's queue (no window required). Raises
/// <see cref="Pressed"/> on a threadpool thread so a slow handler can't stall the loop.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int HOTKEY_ID = 1;

    public event Action? Pressed;

    private readonly uint _mods;
    private readonly uint _vk;
    private Thread? _thread;
    private uint _threadId;
    public bool IsRegistered { get; private set; }

    public GlobalHotkey(HotkeyModifiers modifiers, uint virtualKey)
    {
        _mods = (uint)modifiers | MOD_NOREPEAT;
        _vk = virtualKey;
    }

    /// <summary>Registers the hotkey. Returns false if the combination is unavailable.</summary>
    public bool Start()
    {
        using var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            IsRegistered = RegisterHotKey(IntPtr.Zero, HOTKEY_ID, _mods, _vk);
            ready.Set();
            if (!IsRegistered) return;

            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_HOTKEY)
                {
                    var handler = Pressed;
                    if (handler is not null) Task.Run(handler);
                }
            }
            UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
        })
        { IsBackground = true, Name = "global-hotkey" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(2000);
        return IsRegistered;
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(1000);
        _threadId = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
