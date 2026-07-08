using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Clipper.Core;

/// <summary>Small helpers over public (no-auth) Roblox web endpoints.</summary>
public static class RobloxApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    /// <summary>Opens a Roblox user's profile in the browser. Looks up the user id by exact
    /// username, falling back to a people-search page if the lookup fails or has no match.</summary>
    public static async Task OpenProfileAsync(string username)
    {
        username = username.Trim();
        if (string.IsNullOrEmpty(username)) return;

        string url = $"https://www.roblox.com/search/users?keyword={Uri.EscapeDataString(username)}";
        try
        {
            var payload = JsonSerializer.Serialize(new { usernames = new[] { username }, excludeBannedUsers = false });
            using var body = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync("https://users.roblox.com/v1/usernames/users", body);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0
                    && data[0].TryGetProperty("id", out var idEl))
                {
                    url = $"https://www.roblox.com/users/{idEl.GetInt64()}/profile";
                }
            }
        }
        catch { /* keep the search URL fallback */ }

        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
