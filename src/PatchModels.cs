using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace ReforgedPatchDownloaderApp;

public sealed class ProjectCatalog
{
    public ProjectRelease Release { get; set; } = new();
    public List<PatchOption> Patches { get; set; } = [];
}

public sealed class ProjectRelease
{
    public string StableVersion { get; set; } = "Unknown";
    public string ReleaseDate { get; set; } = "Unknown";
    public string Status { get; set; } = "Live";
    public List<string> UpdatedPatchIds { get; set; } = [];
}

public sealed class PatchOption
{
    public string PatchId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string Variant { get; set; } = "Standard";
    public string Category { get; set; } = "";
    public string Summary { get; set; } = "";
    public string RuleSummary { get; set; } = "";
    public string Requirements { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string LatestVersion { get; set; } = "Unlisted";
    public string ReleaseDate { get; set; } = "Unknown";
    public bool SiteUpdated { get; set; }
    public long RemoteSizeBytes { get; set; }
    public List<string> RequiredPatchIds { get; set; } = [];
    public List<string> LinkedPatchIds { get; set; } = [];

    [JsonIgnore]
    public string FileName => Path.GetFileName(new Uri(DownloadUrl).AbsolutePath);
}

public sealed class RemoteFileMetadata
{
    public string ETag { get; set; } = "";
    public string LastModifiedUtc { get; set; } = "";
    public long ContentLength { get; set; }
}

public sealed class PatchManifestEntry
{
    public string DownloadUrl { get; set; } = "";
    public string PatchId { get; set; } = "";
    public string Variant { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Version { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public string DownloadedUtc { get; set; } = "";
    public string ETag { get; set; } = "";
    public string LastModifiedUtc { get; set; } = "";
    public long ContentLength { get; set; }
}

public sealed class PatchManifest
{
    public List<PatchManifestEntry> Downloads { get; set; } = [];
}

public sealed class PatchRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _status = "Not downloaded";
    private string _installedVersion = "-";
    private string _downloadedUtc = "-";
    private Brush _statusBrush = Brushes.White;
    private string _localSizeText = "-";
    private string _remoteSizeText = "-";
    private string _recommendationText = "-";
    private Brush _recommendationBrush = Brushes.Transparent;

    public PatchOption Patch { get; init; } = new();

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string PatchName => Patch.Name;
    public string Description => Patch.Title;
    public string Variant => Patch.Variant;
    public string LatestVersion => Patch.LatestVersion;
    public string UpdatedLabel => Patch.SiteUpdated ? "Updated" : "";
    public string ReleaseDate => Patch.ReleaseDate;
    public string Category => Patch.Category;
    public string RuleSummary => Patch.RuleSummary;
    public string Summary => Patch.Summary;

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            OnPropertyChanged();
        }
    }

    public string InstalledVersion
    {
        get => _installedVersion;
        set
        {
            if (_installedVersion == value)
            {
                return;
            }

            _installedVersion = value;
            OnPropertyChanged();
        }
    }

    public string DownloadedUtc
    {
        get => _downloadedUtc;
        set
        {
            if (_downloadedUtc == value)
            {
                return;
            }

            _downloadedUtc = value;
            OnPropertyChanged();
        }
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        set
        {
            if (_statusBrush == value)
            {
                return;
            }

            _statusBrush = value;
            OnPropertyChanged();
        }
    }

    public string LocalSizeText
    {
        get => _localSizeText;
        set
        {
            if (_localSizeText == value)
            {
                return;
            }

            _localSizeText = value;
            OnPropertyChanged();
        }
    }

    public string RemoteSizeText
    {
        get => _remoteSizeText;
        set
        {
            if (_remoteSizeText == value)
            {
                return;
            }

            _remoteSizeText = value;
            OnPropertyChanged();
        }
    }

    public string RecommendationText
    {
        get => _recommendationText;
        set
        {
            if (_recommendationText == value)
            {
                return;
            }

            _recommendationText = value;
            OnPropertyChanged();
        }
    }

    public Brush RecommendationBrush
    {
        get => _recommendationBrush;
        set
        {
            if (_recommendationBrush == value)
            {
                return;
            }

            _recommendationBrush = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ActivityLogEntry
{
    public string Timestamp { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class UpdateSummaryItem
{
    public string Title { get; init; } = "";
    public string VersionChange { get; init; } = "";
    public string StatusText { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
}

public sealed class MainViewState : INotifyPropertyChanged
{
    private string _statusText = "Ready";
    private string _folderPath = @"C:\Games\Patches";
    private string _stableVersion = "--";
    private string _siteUpdateDate = "--";
    private string _selectedCount = "0";
    private string _updateAlertText = "No downloaded patches need updates";
    private string _searchText = "";
    private string _selectedCategory = "All";
    private string _selectedStatus = "All";
    private string _detailsText = "Select a patch to inspect its version, rules, and local update state.";
    private double _downloadProgressValue;
    private double _downloadProgressMax = 1;
    private string _downloadProgressText = "No download in progress.";
    private bool _isDownloadInProgress;
    private string _lastCheckedText = "Never";
    private string _updateSummaryText = "No update summary yet.";
    private string _updateSummaryHeaderText = "Everything you have tracked locally is up to date with the current Project Reforged catalog.";
    private bool _canOpenInstalledFile;
    private string _selectedPatchPath = "";

    public ObservableCollection<PatchRow> VisibleRows { get; } = [];
    public ObservableCollection<ActivityLogEntry> Activity { get; } = [];
    public ObservableCollection<UpdateSummaryItem> UpdateItems { get; } = [];
    public ObservableCollection<string> Categories { get; } = ["All", "Core", "Optional", "Audio", "Ultra"];
    public ObservableCollection<string> Statuses { get; } = ["All", "Update available", "Up to date", "Downloaded", "Not downloaded", "Other variant installed"];

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public string FolderPath
    {
        get => _folderPath;
        set => SetField(ref _folderPath, value);
    }

    public string StableVersion
    {
        get => _stableVersion;
        set => SetField(ref _stableVersion, value);
    }

    public string SiteUpdateDate
    {
        get => _siteUpdateDate;
        set => SetField(ref _siteUpdateDate, value);
    }

    public string SelectedCount
    {
        get => _selectedCount;
        set => SetField(ref _selectedCount, value);
    }

    public string UpdateAlertText
    {
        get => _updateAlertText;
        set => SetField(ref _updateAlertText, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetField(ref _searchText, value);
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set => SetField(ref _selectedCategory, value);
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set => SetField(ref _selectedStatus, value);
    }

    public string DetailsText
    {
        get => _detailsText;
        set => SetField(ref _detailsText, value);
    }

    public double DownloadProgressValue
    {
        get => _downloadProgressValue;
        set => SetField(ref _downloadProgressValue, value);
    }

    public double DownloadProgressMax
    {
        get => _downloadProgressMax;
        set => SetField(ref _downloadProgressMax, value);
    }

    public string DownloadProgressText
    {
        get => _downloadProgressText;
        set => SetField(ref _downloadProgressText, value);
    }

    public bool IsDownloadInProgress
    {
        get => _isDownloadInProgress;
        set => SetField(ref _isDownloadInProgress, value);
    }

    public string LastCheckedText
    {
        get => _lastCheckedText;
        set => SetField(ref _lastCheckedText, value);
    }

    public string UpdateSummaryText
    {
        get => _updateSummaryText;
        set => SetField(ref _updateSummaryText, value);
    }

    public string UpdateSummaryHeaderText
    {
        get => _updateSummaryHeaderText;
        set => SetField(ref _updateSummaryHeaderText, value);
    }

    public bool CanOpenInstalledFile
    {
        get => _canOpenInstalledFile;
        set => SetField(ref _canOpenInstalledFile, value);
    }

    public string SelectedPatchPath
    {
        get => _selectedPatchPath;
        set => SetField(ref _selectedPatchPath, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class DownloadProgressInfo
{
    public long BytesReceived { get; init; }
    public long? TotalBytes { get; init; }
}

public static class DisplayFormatting
{
    public static string FormatBytes(long? bytes)
    {
        if (bytes is null || bytes < 0)
        {
            return "-";
        }

        var value = (double)bytes.Value;
        var units = new[] { "B", "KB", "MB", "GB" };
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return value >= 100 || unitIndex == 0
            ? value.ToString("0") + " " + units[unitIndex]
            : value.ToString("0.0") + " " + units[unitIndex];
    }

    public static string FormatLastChecked(string utcText)
    {
        if (string.IsNullOrWhiteSpace(utcText) || !DateTime.TryParse(utcText, out var parsed))
        {
            return "Never";
        }

        return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    public static Brush RecommendationBrushFor(string text)
    {
        return text == "-" || string.IsNullOrWhiteSpace(text)
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromRgb(225, 240, 255));
    }
}
