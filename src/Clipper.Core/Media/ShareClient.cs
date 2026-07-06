using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Clipper.Core;

/// <summary>Uploads a clip to the Clipper share server and returns its public watch URL.</summary>
public static class ShareClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };

    public static async Task<string> UploadAsync(
        string endpoint, string token, string filePath, string title,
        int width = 0, int height = 0, CancellationToken ct = default)
    {
        string url = endpoint.TrimEnd('/') + "/api/upload";
        await using var stream = File.OpenRead(filePath);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.TryAddWithoutValidation("X-Title", Uri.EscapeDataString(title));
        if (width > 0) req.Headers.TryAddWithoutValidation("X-Width", width.ToString());
        if (height > 0) req.Headers.TryAddWithoutValidation("X-Height", height.ToString());

        using var resp = await Http.SendAsync(req, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Upload failed ({(int)resp.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("url").GetString()
               ?? throw new InvalidOperationException("Server did not return a URL.");
    }
}
