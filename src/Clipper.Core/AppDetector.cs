using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Clipper.Core;

/// <summary>Detects the foreground app/game (used to auto-title clips).</summary>
public static class AppDetector
{
    private static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "Clipper.App", "ApplicationFrameHost", "SearchHost", "SearchApp",
        "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost", "dwm", "LockApp",
    };

    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RobloxPlayerBeta"] = "Roblox",
        ["FortniteClient-Win64-Shipping"] = "Fortnite",
        ["VALORANT-Win64-Shipping"] = "Valorant",
        ["gta5"] = "GTA V",
        ["GTA5_Enhanced"] = "GTA V",
        ["csgo"] = "CS:GO",
        ["cs2"] = "CS2",
        ["LeagueofLegends"] = "League of Legends",
        ["Minecraft"] = "Minecraft",
        ["javaw"] = "Minecraft",
        ["FestivalClient-Win64-Shipping"] = "Fortnite Festival",
    };

    /// <summary>A friendly name for the current foreground game/app, or null if it's the shell/us.</summary>
    public static string? ForegroundGame()
    {
        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero) return null;
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == 0) return null;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            string name = proc.ProcessName;
            if (Ignore.Contains(name)) return null;
            if (Known.TryGetValue(name, out var friendly)) return friendly;

            string title = GetWindowTitle(h);
            if (!string.IsNullOrWhiteSpace(title) && title.Length is > 1 and <= 40)
                return title.Trim();
            return name;
        }
        catch { return null; }
    }

    private static string GetWindowTitle(IntPtr h)
    {
        int len = GetWindowTextLength(h);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
}
