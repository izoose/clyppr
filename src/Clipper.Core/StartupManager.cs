using Microsoft.Win32;

namespace Clipper.Core;

/// <summary>Manages the "run Clipper when Windows starts" registry entry (HKCU Run key).</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Clipper";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
        catch { return false; }
    }

    public static void Set(bool enabled, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key!.SetValue(ValueName, $"\"{exePath}\" --tray");
            else key!.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* best effort */ }
    }
}
