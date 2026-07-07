using System.Diagnostics;
using System.Runtime.InteropServices;
using static Clipper.Engine.Native;

namespace Clipper.Engine;

/// <summary>Enumerates the apps that currently have an active audio render session, so each can
/// be captured on its own track (per-app audio separation without manual configuration).</summary>
public static class AudioSessions
{
    private static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    private const int AudioSessionStateActive = 1;

    /// <summary>Distinct processes with an ACTIVE audio render session (excluding system sounds and <paramref name="excludePid"/>).</summary>
    public static List<(uint Pid, string Name)> ActiveApps(uint excludePid)
    {
        var result = new List<(uint, string)>();
        try
        {
            var enumr = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator)!)!;
            if (enumr.GetDefaultAudioEndpoint(EDataFlow_eRender, ERole_eConsole, out IMMDevice dev) != 0) return result;
            if (dev.Activate(IID_IAudioSessionManager2, CLSCTX_ALL, IntPtr.Zero, out object mgrObj) != 0) return result;
            var mgr = (IAudioSessionManager2)mgrObj;
            if (mgr.GetSessionEnumerator(out var sessions) != 0) return result;
            sessions.GetCount(out int count);

            var seen = new HashSet<uint>();
            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var ctl) != 0 || ctl is null) continue;
                if (ctl.GetState(out int state) != 0 || state != AudioSessionStateActive) continue;
                if (ctl is not IAudioSessionControl2 c2) continue;
                if (c2.IsSystemSoundsSession() == 0) continue;      // 0 = S_OK = system sounds → skip
                if (c2.GetProcessId(out uint pid) != 0 || pid == 0 || pid == excludePid) continue;
                if (!seen.Add(pid)) continue;
                try { using var proc = Process.GetProcessById((int)pid); result.Add((pid, proc.ProcessName)); }
                catch { }
            }
        }
        catch { }
        return result;
    }
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionManager2
{
    // IAudioSessionManager (2 slots) — unused placeholders
    [PreserveSig] int GetAudioSessionControl();
    [PreserveSig] int GetSimpleAudioVolume();
    // IAudioSessionManager2
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator ppSessionEnum);
    [PreserveSig] int RegisterSessionNotification(IntPtr n);
    [PreserveSig] int UnregisterSessionNotification(IntPtr n);
    [PreserveSig] int RegisterDuckNotification(IntPtr a, IntPtr b);
    [PreserveSig] int UnregisterDuckNotification(IntPtr n);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int sessionCount);
    [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
}

[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionControl
{
    [PreserveSig] int GetState(out int state);
    [PreserveSig] int GetDisplayName(out IntPtr name);
    [PreserveSig] int SetDisplayName();
    [PreserveSig] int GetIconPath(out IntPtr path);
    [PreserveSig] int SetIconPath();
    [PreserveSig] int GetGroupingParam(out Guid group);
    [PreserveSig] int SetGroupingParam();
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionControl2
{
    // IAudioSessionControl (9 slots) — placeholders
    [PreserveSig] int GetState(out int state);
    [PreserveSig] int GetDisplayName(out IntPtr name);
    [PreserveSig] int SetDisplayName();
    [PreserveSig] int GetIconPath(out IntPtr path);
    [PreserveSig] int SetIconPath();
    [PreserveSig] int GetGroupingParam(out Guid group);
    [PreserveSig] int SetGroupingParam();
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
    // IAudioSessionControl2
    [PreserveSig] int GetSessionIdentifier(out IntPtr id);
    [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
    [PreserveSig] int GetProcessId(out uint pid);
    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference(bool optOut);
}
