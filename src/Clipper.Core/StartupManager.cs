using Microsoft.Win32;

namespace Clipper.Core;

/// <summary>Manages the "run Clyppr when Windows starts" registry entry (HKCU Run key).</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Clyppr";
    private const string LegacyValueName = "Clipper";   // pre-rebrand entry, cleaned up on write

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null || key?.GetValue(LegacyValueName) is not null;
        }
        catch { return false; }
    }

    public static void Set(bool enabled, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            key!.DeleteValue(LegacyValueName, throwOnMissingValue: false);   // drop any old "Clipper" entry
            if (enabled) key.SetValue(ValueName, $"\"{exePath}\" --tray");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* best effort */ }
    }
}
