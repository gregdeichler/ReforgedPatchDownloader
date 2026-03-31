using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReforgedPatchDownloaderApp;

public sealed class PatchDownloaderService
{
    private const string HomeUrl = "https://projectreforged.github.io/";
    private const string DownloadsUrl = "https://projectreforged.github.io/downloads/";
    private const string ManifestFileName = ".project-reforged-manifest-v2.json";

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public PatchDownloaderService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(45);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ProjectReforgedPatchDownloader/2.0");
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<ProjectCatalog> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var homeHtml = await _httpClient.GetStringAsync(HomeUrl, cancellationToken);
        var downloadsHtml = await _httpClient.GetStringAsync(DownloadsUrl, cancellationToken);

        var release = ParseReleaseInfo(NormalizeHtmlToLines(homeHtml));
        var patches = ParsePatches(NormalizeHtmlToLines(downloadsHtml), release);

        return new ProjectCatalog
        {
            Release = release,
            Patches = patches
        };
    }

    public async Task<Dictionary<string, RemoteFileMetadata>> LoadRemoteMetadataAsync(IEnumerable<PatchOption> patches, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, RemoteFileMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var patch in patches)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, patch.DownloadUrl);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                metadata[patch.DownloadUrl] = new RemoteFileMetadata
                {
                    ETag = response.Headers.ETag?.Tag ?? "",
                    LastModifiedUtc = response.Content.Headers.LastModified?.UtcDateTime.ToString("O") ?? "",
                    ContentLength = response.Content.Headers.ContentLength ?? 0
                };
            }
            catch
            {
            }
        }

        return metadata;
    }

    public async Task DownloadPatchAsync(PatchOption patch, string folderPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folderPath);
        var destinationPath = Path.Combine(folderPath, patch.FileName);

        using var response = await _httpClient.GetAsync(patch.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    public async Task<PatchManifest> LoadManifestAsync(string folderPath, CancellationToken cancellationToken)
    {
        var path = GetManifestPath(folderPath);
        if (!File.Exists(path))
        {
            return new PatchManifest();
        }

        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<PatchManifest>(stream, _jsonOptions, cancellationToken);
        return manifest ?? new PatchManifest();
    }

    public async Task SaveManifestAsync(string folderPath, PatchManifest manifest, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folderPath);
        var path = GetManifestPath(folderPath);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, _jsonOptions, cancellationToken);
    }

    public string GetManifestPath(string folderPath)
    {
        return Path.Combine(folderPath, ManifestFileName);
    }

    private static ProjectRelease ParseReleaseInfo(List<string> lines)
    {
        var release = new ProjectRelease();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Contains("Current Stable", StringComparison.OrdinalIgnoreCase))
            {
                var versionMatch = Regex.Match(line, @"v\d+(?:\.\d+)+", RegexOptions.IgnoreCase);
                if (versionMatch.Success)
                {
                    release.StableVersion = versionMatch.Value;
                }
                else if (i + 1 < lines.Count)
                {
                    versionMatch = Regex.Match(lines[i + 1], @"v\d+(?:\.\d+)+", RegexOptions.IgnoreCase);
                    if (versionMatch.Success)
                    {
                        release.StableVersion = versionMatch.Value;
                    }
                }
            }

            if (line.StartsWith("Status", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split([':', '•'], 2);
                if (parts.Length == 2)
                {
                    release.Status = parts[1].Trim();
                }
            }

            if (line.Contains("Updated:", StringComparison.OrdinalIgnoreCase))
            {
                var dateMatch = Regex.Match(line, @"\d{4}-\d{2}-\d{2}");
                if (dateMatch.Success)
                {
                    release.ReleaseDate = dateMatch.Value;
                }
            }

            if (line.Contains("Updated Modules", StringComparison.OrdinalIgnoreCase))
            {
                release.UpdatedPatchIds = Regex.Matches(line.ToUpperInvariant(), @"\b[A-Z]\b")
                    .Select(match => match.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        return release;
    }

    private static List<PatchOption> ParsePatches(List<string> lines, ProjectRelease release)
    {
        var sections = new List<PatchSection>();
        var currentCategory = "Optional";
        PatchSection? currentSection = null;

        foreach (var line in lines)
        {
            if (IsCategoryHeading(line))
            {
                currentCategory = line;
                continue;
            }

            var heading = ExtractPatchHeading(line);
            if (heading is not null)
            {
                currentSection = new PatchSection
                {
                    Heading = heading,
                    Category = currentCategory
                };
                sections.Add(currentSection);
                continue;
            }

            currentSection?.Lines.Add(line);
        }

        return sections.SelectMany(section => CreateOptions(section, release)).OrderBy(option => option.PatchId).ThenBy(option => option.Variant).ToList();
    }

    private static IEnumerable<PatchOption> CreateOptions(PatchSection section, ProjectRelease release)
    {
        var headingMatch = Regex.Match(section.Heading, @"(PATCH-[A-Z])(?:\s*[-—:]\s*|\s+)?(.*)", RegexOptions.IgnoreCase);
        if (!headingMatch.Success)
        {
            yield break;
        }

        var patchName = headingMatch.Groups[1].Value.ToUpperInvariant();
        var patchId = patchName[^1].ToString();
        var title = string.IsNullOrWhiteSpace(headingMatch.Groups[2].Value) ? patchName : headingMatch.Groups[2].Value.Trim();
        var downloadIndexes = section.Lines
            .Select((line, index) => new { line, index })
            .Where(item => item.line.Contains("Download [", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index)
            .ToList();

        if (downloadIndexes.Count == 0)
        {
            yield break;
        }

        var summary = FindSummary(section.Lines);
        var requirements = BuildRequirements(section.Lines);
        var requiredPatches = ParsePatchIds(requirements, "Requires");
        var linkedPatches = ParsePatchIds(requirements, "Install with");

        for (var optionIndex = 0; optionIndex < downloadIndexes.Count; optionIndex++)
        {
            var line = section.Lines[downloadIndexes[optionIndex]];
            var urlMatch = Regex.Match(line, @"\[(https?://[^\]]+)\]", RegexOptions.IgnoreCase);
            if (!urlMatch.Success)
            {
                continue;
            }

            var variant = ResolveVariant(section.Lines, downloadIndexes[optionIndex], optionIndex, title);
            var versionMatch = Regex.Match(line, @"v\d+(?:\.\d+)+", RegexOptions.IgnoreCase);
            var ruleSummary = BuildRuleSummary(patchId, requiredPatches, linkedPatches);

            yield return new PatchOption
            {
                PatchId = patchId,
                Name = patchName,
                Title = title,
                Variant = variant,
                Category = NormalizeCategory(section.Category),
                Summary = summary,
                Requirements = requirements,
                RuleSummary = ruleSummary,
                DownloadUrl = urlMatch.Groups[1].Value,
                LatestVersion = versionMatch.Success ? versionMatch.Value : "Unlisted",
                ReleaseDate = release.ReleaseDate,
                SiteUpdated = release.UpdatedPatchIds.Contains(patchId, StringComparer.OrdinalIgnoreCase),
                RequiredPatchIds = requiredPatches,
                LinkedPatchIds = linkedPatches
            };
        }
    }

    private static string ResolveVariant(List<string> lines, int downloadIndex, int optionIndex, string title)
    {
        if (title.Contains("Compatible Build", StringComparison.OrdinalIgnoreCase))
        {
            return "Compatible";
        }

        for (var i = downloadIndex - 1; i >= 0; i--)
        {
            var candidate = lines[i];
            if (candidate.Contains("Download [", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (candidate.Equals("Standard", StringComparison.OrdinalIgnoreCase))
            {
                return "Standard";
            }

            if (candidate.EndsWith("Version", StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Replace(" Version", "", StringComparison.OrdinalIgnoreCase).Trim();
            }
        }

        return optionIndex == 0 ? "Standard" : "Alternative";
    }

    private static string FindSummary(List<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.Contains("Download [", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("Requires", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Install with", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Install only", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Do not install", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Optional", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Core", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("by ", StringComparison.OrdinalIgnoreCase)
                || line.Length < 20)
            {
                continue;
            }

            return line;
        }

        return "Project Reforged patch module.";
    }

    private static string BuildRequirements(List<string> lines)
    {
        var matches = lines.Where(line =>
            line.StartsWith("Requires", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Install with", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Install only", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Do not install", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Use this only", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Optional (conditional)", StringComparison.OrdinalIgnoreCase)).ToList();

        return matches.Count == 0
            ? "No extra install rules were published on the downloads page."
            : string.Join(" ", matches);
    }

    private static List<string> ParsePatchIds(string requirements, string cue)
    {
        if (!requirements.Contains(cue, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var ids = Regex.Matches(requirements.ToUpperInvariant(), @"PATCH-([A-Z])")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count > 0)
        {
            return ids;
        }

        return Regex.Matches(requirements.ToUpperInvariant(), @"\b([A-Z])\b")
            .Select(match => match.Groups[1].Value)
            .Where(value => value is not "V")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildRuleSummary(string patchId, IReadOnlyCollection<string> requiredPatches, IReadOnlyCollection<string> linkedPatches)
    {
        if (patchId is "B" or "D" or "E")
        {
            return "B/D/E linked";
        }

        if (patchId is "L" or "U")
        {
            return "Single choice";
        }

        if (requiredPatches.Count > 0)
        {
            return "Needs " + string.Join("+", requiredPatches);
        }

        if (linkedPatches.Count > 0)
        {
            return "Linked";
        }

        return "-";
    }

    private static string NormalizeCategory(string category)
    {
        return category switch
        {
            "Core Modules" => "Core",
            "Optional Enhancements" => "Optional",
            "Ultra Tier" => "Ultra",
            _ => category
        };
    }

    private static bool IsCategoryHeading(string line)
    {
        return line is "Core Modules" or "Optional Enhancements" or "Audio" or "Ultra Tier";
    }

    private static string? ExtractPatchHeading(string line)
    {
        var match = Regex.Match(line, @"^\s*[^A-Za-z0-9]*(PATCH-[A-Z])(?:\s*[-—:]\s*|\s+)?(.*)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var patchName = match.Groups[1].Value.ToUpperInvariant();
        var title = match.Groups[2].Value.Trim();
        return string.IsNullOrWhiteSpace(title) ? patchName : patchName + " - " + title;
    }

    private static List<string> NormalizeHtmlToLines(string html)
    {
        var withLinks = Regex.Replace(
            html,
            "<a\\b[^>]*href\\s*=\\s*\"([^\"]+)\"[^>]*>(.*?)</a>",
            match =>
            {
                var href = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
                var text = Regex.Replace(match.Groups[2].Value, "<.*?>", string.Empty);
                text = System.Net.WebUtility.HtmlDecode(text);
                text = CollapseWhitespace(text);
                return text + " [" + href + "]";
            },
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var withBreaks = Regex.Replace(
            withLinks,
            "(</(p|div|section|article|header|main|nav|li|ul|ol|h1|h2|h3|h4|h5|h6|table|tr|td|blockquote)>|<br\\s*/?>)",
            "\n",
            RegexOptions.IgnoreCase);

        var withoutTags = Regex.Replace(withBreaks, "<.*?>", string.Empty, RegexOptions.Singleline);
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);

        return decoded.Replace("\r", "")
            .Split('\n')
            .Select(CollapseWhitespace)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private static string CollapseWhitespace(string value)
    {
        return Regex.Replace(value ?? string.Empty, "\\s+", " ").Trim();
    }
}

internal sealed class PatchSection
{
    public string Heading { get; set; } = "";
    public string Category { get; set; } = "";
    public List<string> Lines { get; set; } = [];
}
