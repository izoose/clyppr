using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clipper.Core;

/// <summary>
/// Figures out which Roblox experience the user is in by reading the Roblox client logs
/// (%LOCALAPPDATA%\Roblox\logs), then resolves the human-readable name via a public no-auth
/// endpoint. Used to title clips by the real game instead of just "Roblox".
/// </summary>
public static class RobloxExperience
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    // FLog tokens can drift across client versions — match the stable substrings.
    private static readonly Regex UniverseRe = new(@"game_join_loadtime:.*?universeid:(\d+)", RegexOptions.Compiled);
    private static readonly Dictionary<long, string> NameCache = new();

    private static string LogsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");

    /// <summary>UniverseId of the most recently joined experience (from the newest log), or null.</summary>
    public static long? CurrentUniverseId()
    {
        try
        {
            if (!Directory.Exists(LogsDir)) return null;
            var newest = new DirectoryInfo(LogsDir).GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (newest is null) return null;

            long? uid = null;
            using var fs = new FileStream(newest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            string? line;
            while ((line = sr.ReadLine()) is not null)
            {
                var m = UniverseRe.Match(line);
                if (m.Success && long.TryParse(m.Groups[1].Value, out long u)) uid = u;   // keep the LAST match
            }
            return uid;
        }
        catch { return null; }
    }

    /// <summary>Resolves (and caches) an experience name from a universeId. Public, no auth.</summary>
    public static async Task<string?> ResolveNameAsync(long universeId)
    {
        if (NameCache.TryGetValue(universeId, out var cached)) return cached;
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://games.roblox.com/v1/games?universeIds={universeId}"));
            if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0
                && data[0].TryGetProperty("name", out var nameEl))
            {
                string? name = nameEl.GetString();
                if (!string.IsNullOrWhiteSpace(name)) { NameCache[universeId] = name!; return name; }
            }
        }
        catch { }
        return null;
    }
}
