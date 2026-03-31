using System.IO;
using System.Text.Json;

namespace ReforgedPatchDownloaderApp;

public sealed class AppSettings
{
    public string FolderPath { get; set; } = @"C:\Games\Patches";
    public string LastCheckedUtc { get; set; } = "";
    public Dictionary<string, double> ColumnWidths { get; set; } = [];
    public List<string> SelectedPatchUrls { get; set; } = [];
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string GetSettingsPath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProjectReforgedPatchDownloader");
        Directory.CreateDirectory(root);
        return Path.Combine(root, "settings.json");
    }

    public static async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(path);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
        return settings ?? new AppSettings();
    }

    public static async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var path = GetSettingsPath();
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }
}
