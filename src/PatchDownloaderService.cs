using System.IO;
using System.Net.Http;
using System.Text.Json;

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
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ProjectReforgedPatchDownloader/2.3");
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
        return PatchCatalogParser.ParseCatalog(homeHtml, downloadsHtml);
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

    public async Task DownloadPatchAsync(PatchOption patch, string folderPath, IProgress<DownloadProgressInfo>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folderPath);
        var destinationPath = Path.Combine(folderPath, patch.FileName);

        using var response = await _httpClient.GetAsync(patch.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);
        var totalBytes = response.Content.Headers.ContentLength;
        var buffer = new byte[81920];
        long received = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            progress?.Report(new DownloadProgressInfo
            {
                BytesReceived = received,
                TotalBytes = totalBytes
            });
        }
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
}
