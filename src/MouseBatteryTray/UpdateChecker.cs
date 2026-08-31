using System.Net.Http;
using System.Text.Json;

namespace MouseBatteryTray;

/// <summary>Best-effort check against the GitHub Releases API — never throws, never blocks startup.</summary>
public static class UpdateChecker
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/flowercircle0304/mousebatteryUI/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/flowercircle0304/mousebatteryUI/releases/latest";

    public sealed record UpdateInfo(string LatestVersion, string HtmlUrl);

    public static async Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MouseBatteryTray-UpdateCheck");

            using var resp = await http.GetAsync(ReleasesApiUrl, ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(htmlUrl)) htmlUrl = ReleasesPageUrl;

            string latest = tag.TrimStart('v', 'V');
            if (!Version.TryParse(Normalize(latest), out var latestVer)) return null;
            if (!Version.TryParse(Normalize(currentVersion), out var currentVer)) return null;

            return latestVer > currentVer ? new UpdateInfo(latest, htmlUrl) : null;
        }
        catch
        {
            return null; // offline, rate-limited, malformed response, etc. — silently skip
        }
    }

    private static string Normalize(string v) => v.Split('.').Length switch
    {
        1 => v + ".0.0",
        2 => v + ".0",
        _ => v,
    };
}
