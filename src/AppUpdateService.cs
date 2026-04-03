using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReforgedPatchDownloaderApp;

public sealed class AppUpdateService
{
    private readonly HttpClient _httpClient;

    public AppUpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ProjectReforgedPatchDownloader/2.3");
    }

    public async Task<AppUpdateInfo?> LoadLatestReleaseAsync(string releaseApiUrl, string fallbackReleasePageUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(releaseApiUrl))
        {
            return null;
        }

        await using var stream = await _httpClient.GetStreamAsync(releaseApiUrl, cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<GitHubReleasePayload>(stream, cancellationToken: cancellationToken);
        if (payload is null)
        {
            return null;
        }

        return new AppUpdateInfo
        {
            Version = NormalizeVersion(payload.TagName),
            ReleasePageUrl = string.IsNullOrWhiteSpace(payload.HtmlUrl) ? fallbackReleasePageUrl : payload.HtmlUrl,
            PublishedUtc = payload.PublishedAt ?? "",
            Summary = string.IsNullOrWhiteSpace(payload.Name) ? payload.Body ?? "" : payload.Name
        };
    }

    public static bool IsNewerVersion(string currentVersion, string candidateVersion)
    {
        if (TryParseVersion(candidateVersion, out var candidate) && TryParseVersion(currentVersion, out var current))
        {
            return candidate > current;
        }

        return !string.IsNullOrWhiteSpace(candidateVersion)
            && !string.Equals(candidateVersion, currentVersion, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeVersion(string versionText)
    {
        return string.IsNullOrWhiteSpace(versionText)
            ? ""
            : versionText.Trim().TrimStart('v', 'V');
    }

    private static bool TryParseVersion(string versionText, out Version version)
    {
        var normalized = NormalizeVersion(versionText);
        if (Version.TryParse(normalized, out var parsed) && parsed is not null)
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0);
        return false;
    }

    private sealed class GitHubReleasePayload
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("published_at")]
        public string? PublishedAt { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }
    }
}
