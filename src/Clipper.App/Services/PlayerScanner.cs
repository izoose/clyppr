using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace Clipper.App;

/// <summary>
/// Detects Roblox player usernames in a clip: samples upscaled frames, OCRs them, and pulls out
/// overhead nametags. Roblox names show as a display name (often no "@"), so we accept plain
/// tokens too — filtered by charset, a digit/underscore signal (almost every Roblox name has one),
/// a HUD region cut (skip the speedometer/UI band), and a small stop-list.
/// </summary>
public static class PlayerScanner
{
    private const double IntervalSec = 2.5;   // sample a frame every 2.5s
    private static readonly Regex NameLike = new(@"^[A-Za-z0-9_]{3,20}$", RegexOptions.Compiled);

    // common HUD / UI words that pass the charset filter but aren't players
    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "roblox", "passengers", "controls", "compact", "auto", "speed", "menu", "settings",
        "loading", "reset", "cancel", "confirm", "purchase", "vehicle", "spawn", "premium",
        "level", "cash", "money", "health", "stamina", "inventory", "shop", "store",
    };

    public static async Task<List<(string Name, double Time)>> ScanAsync(string clipPath, string ffmpegPath = "ffmpeg")
    {
        var firstSeen = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var hitCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(clipPath)) return new();

        OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages()
                            ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en"));
        if (engine is null) return new();

        string tmp = Path.Combine(Path.GetTempPath(), "clyppr_ocr_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            // upscale 1.5x with lanczos so small nametags stay legible to OCR
            await Task.Run(() => RunFfmpeg(ffmpegPath,
                $"-y -hide_banner -loglevel error -i \"{clipPath}\" -vf \"fps=1/{IntervalSec.ToString(System.Globalization.CultureInfo.InvariantCulture)},scale=iw*1.5:ih*1.5:flags=lanczos\" -qscale:v 2 \"{Path.Combine(tmp, "f%03d.jpg")}\""));

            foreach (var img in Directory.GetFiles(tmp, "*.jpg").OrderBy(p => p).Take(40))
            {
                double time = FrameTime(img);
                try
                {
                    var bitmap = await LoadBitmapAsync(img);
                    double h = bitmap.PixelHeight;
                    var result = await engine.RecognizeAsync(bitmap);
                    foreach (var line in result.Lines)
                    {
                        foreach (var word in line.Words)
                        {
                            double cy = (word.BoundingRect.Y + word.BoundingRect.Height / 2) / h;
                            if (cy > 0.82 || cy < 0.05) continue;   // skip bottom HUD / top game bar

                            if (!TryUsername(word.Text, out string tok)) continue;
                            hitCount[tok] = hitCount.GetValueOrDefault(tok) + 1;
                            if (!firstSeen.ContainsKey(tok)) firstSeen[tok] = time;
                        }
                    }
                    bitmap.Dispose();
                }
                catch { /* skip unreadable frames */ }
            }
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }

        // Merge OCR variants of the same nametag (noauth12 / uth12 / noautht2 …). Process the
        // most-frequent, longest reads first so the cluster keeps the "best" spelling.
        var ordered = firstSeen.Keys
            .OrderByDescending(k => hitCount.GetValueOrDefault(k))
            .ThenByDescending(k => k.Length)
            .ToList();
        var reps = new List<string>();
        foreach (var name in ordered)
            if (!reps.Any(r => SameName(r, name)))
                reps.Add(name);

        return reps.Select(r => (Name: r, Time: firstSeen[r])).OrderBy(x => x.Time).ToList();
    }

    /// <summary>Whether two OCR reads are almost certainly the same nametag.</summary>
    private static bool SameName(string a, string b)
    {
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        string lo = a.Length <= b.Length ? a : b;
        string hi = a.Length <= b.Length ? b : a;
        // one is contained in the other with only a few extra chars (uth12 ⊂ noauth12)
        if (hi.Length - lo.Length <= 4 && hi.Contains(lo, StringComparison.OrdinalIgnoreCase)) return true;
        // small edit distance relative to length (noauth12 vs noautht2)
        int d = Levenshtein(a.ToLowerInvariant(), b.ToLowerInvariant());
        return d <= Math.Max(2, hi.Length / 3);
    }

    private static int Levenshtein(string a, string b)
    {
        var dp = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) dp[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            int prev = dp[0];
            dp[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cur = dp[j];
                dp[j] = Math.Min(Math.Min(dp[j] + 1, dp[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = cur;
            }
        }
        return dp[b.Length];
    }

    private static bool TryUsername(string raw, out string name)
    {
        name = "";
        string t = raw.Trim();
        bool hasAt = t.StartsWith("@");
        string tok = t.TrimStart('@').Trim();

        if (!NameLike.IsMatch(tok)) return false;
        if (!tok.Any(char.IsLetter)) return false;
        if (Stop.Contains(tok)) return false;

        bool hasLower = tok.Any(char.IsLower);
        bool hasDigitOrUnderscore = tok.Any(c => char.IsDigit(c) || c == '_');

        // Accept an explicit @handle, OR a lowercase-bearing name with a digit/underscore
        // (noauth12, Max44, GlazeYo_Dept). This rejects ALL-CAPS weapon codes (AK47, M1911) and
        // capitalized dictionary/UI words (Beretta, Colt, Avenue, Success).
        if (hasAt || (hasLower && hasDigitOrUnderscore)) { name = tok; return true; }
        return false;
    }

    private static double FrameTime(string path)
    {
        var digits = new string(Path.GetFileNameWithoutExtension(path).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int n) && n > 0 ? (n - 1) * IntervalSec : 0;
    }

    private static async Task<SoftwareBitmap> LoadBitmapAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync();
    }

    private static void RunFfmpeg(string exe, string args)
    {
        var psi = new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi);
        p?.WaitForExit();
    }
}
