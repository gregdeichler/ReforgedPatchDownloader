using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace ReforgedPatchDownloaderApp;

public partial class MainWindow : Window
{
    private readonly MainViewState _viewState = new();
    private readonly PatchDownloaderService _service = new();
    private readonly AppUpdateService _appUpdateService = new();
    private readonly ObservableCollection<PatchRow> _allRows = [];

    private ProjectCatalog _catalog = new();
    private Dictionary<string, RemoteFileMetadata> _metadataByUrl = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, PatchManifestEntry> _manifestByUrl = new(StringComparer.OrdinalIgnoreCase);
    private bool _isApplyingSelectionRules;
    private CancellationTokenSource? _downloadCancellation;
    private AppSettings _settings = new();
    private bool _closeAfterDownloadStop;
    private bool _allowImmediateClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewState;
        _viewState.AppVersionText = "App v" + GetCurrentAppVersion();
        _viewState.PropertyChanged += ViewState_PropertyChanged;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadSettingsAsync();
        await ReloadManifestAsync();
        await RefreshCatalogAsync();
        await CheckAppUpdatesAsync(false);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCatalogAsync();
    }

    private async void DownloadSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _allRows.Where(row => row.IsSelected).ToList();
        if (selected.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Choose at least one patch first.", "Nothing Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!ConfirmDownloadFolder(selected))
        {
            return;
        }

        await DownloadRowsAsync(selected, "Selected downloads completed.");
    }

    private void StopDownloads_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadCancellation is null || !_viewState.IsDownloadInProgress)
        {
            return;
        }

        AppendLog("Stopping active downloads...");
        _viewState.DownloadProgressText = "Stopping downloads...";
        _downloadCancellation.Cancel();
    }

    private void SelectCore_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _allRows)
        {
            row.IsSelected = row.Category == "Core";
        }

        EnforceSelectionRules(null);
        RefreshVisibleRows();
    }

    private void SelectRecommended_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _allRows)
        {
            row.IsSelected =
                row.Category == "Core"
                || row.Patch.PatchId is "B" or "D" or "E" or "I" or "M" or "V"
                || (row.Patch.PatchId == "U" && string.Equals(row.Patch.Variant, "Standard", StringComparison.OrdinalIgnoreCase));
        }

        foreach (var row in _allRows.Where(item => item.IsSelected).ToList())
        {
            PromptForRequiredPatches(row, false);
            EnforceSelectionRules(row);
        }

        RefreshVisibleRows();
        AppendLog("Selected recommended baseline patches.");
    }

    private async void ResetTracking_Click(object sender, RoutedEventArgs e)
    {
        var manifestPath = _service.GetManifestPath(_viewState.FolderPath);
        if (!File.Exists(manifestPath))
        {
            System.Windows.MessageBox.Show(this, "No tracking file exists in the selected folder.", "Nothing To Reset", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (System.Windows.MessageBox.Show(this, "Reset downloaded patch tracking for this folder?", "Reset Tracking", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        File.Delete(manifestPath);
        await ReloadManifestAsync();
        ApplyStatuses();
        AppendLog("Tracking manifest removed.");
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog();
        dialog.Description = "Choose your Project Reforged patch folder";
        dialog.ShowNewFolderButton = true;
        if (Directory.Exists(_viewState.FolderPath))
        {
            dialog.SelectedPath = _viewState.FolderPath;
        }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _viewState.FolderPath = dialog.SelectedPath;
            await ReloadManifestAsync();
            ApplyStatuses();
        }
    }

    private void VerifyFiles_Click(object sender, RoutedEventArgs e)
    {
        VerifyLocalFiles(true);
    }

    private void PatchNotes_Click(object sender, RoutedEventArgs e)
    {
        if (PatchGrid.SelectedItem is PatchRow row)
        {
            AppendLog("Opened release notes for " + row.Patch.Name + " [" + row.Patch.Variant + "].");
        }

        OpenUrl(GetSelectedPatchNotesUrl());
    }

    private void ProjectSite_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://projectreforged.github.io/downloads/");
    }

    private async void UpdateInstalled_Click(object sender, RoutedEventArgs e)
    {
        var updateRows = _allRows.Where(row => row.Status == "Update available").ToList();
        if (updateRows.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "No installed patches currently need updates.", "Everything Is Current", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var row in _allRows)
        {
            row.IsSelected = false;
        }

        foreach (var row in updateRows)
        {
            row.IsSelected = true;
            PromptForRequiredPatches(row, false);
            EnforceSelectionRules(row);
        }

        RefreshVisibleRows();

        var selected = _allRows.Where(row => row.IsSelected).ToList();
        if (!ConfirmDownloadFolder(selected))
        {
            return;
        }

        await DownloadRowsAsync(selected, "Installed patches were updated.");
    }

    private async void CheckAppUpdates_Click(object sender, RoutedEventArgs e)
    {
        await CheckAppUpdatesAsync(true);
    }

    private async void RepairSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _allRows.Where(row => row.IsSelected).ToList();
        if (selected.Count == 0 && PatchGrid.SelectedItem is PatchRow activeRow)
        {
            activeRow.IsSelected = true;
            selected.Add(activeRow);
        }

        if (selected.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Select one or more patches to repair first.", "Nothing Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!ConfirmDownloadFolder(selected))
        {
            return;
        }

        AppendLog("Repairing " + selected.Count + " selected patch(es).");
        await DownloadRowsAsync(selected, "Selected patches were repaired.");
    }

    private async void DismissFirstRunHelp_Click(object sender, RoutedEventArgs e)
    {
        _viewState.IsFirstRunHelpVisible = false;
        _settings.HasDismissedFirstRunHelp = true;
        await SaveSettingsAsync();
    }

    private void PatchGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PatchGrid.SelectedItem is PatchRow row)
        {
            ApplySelectedPatch(row);
        }
    }

    private void UpdateSummaryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UpdateSummaryList.SelectedItem is not UpdateSummaryItem item)
        {
            return;
        }

        SelectRowByDownloadUrl(item.DownloadUrl);
        UpdateSummaryList.SelectedItem = null;
    }

    private void OpenInstalledFile_Click(object sender, RoutedEventArgs e)
    {
        if (PatchGrid.SelectedItem is not PatchRow row)
        {
            return;
        }

        var filePath = GetPatchFilePath(row);
        if (!File.Exists(filePath))
        {
            System.Windows.MessageBox.Show(this, "That patch file is not installed in the current folder.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
            ApplySelectedPatch(row);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "/select,\"" + filePath + "\"",
            UseShellExecute = true
        });
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowImmediateClose)
        {
            return;
        }

        if (!_viewState.IsDownloadInProgress)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            "A download is still in progress. Stop the queue and close the app?",
            "Download In Progress",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        _closeAfterDownloadStop = true;
        _downloadCancellation?.Cancel();
        e.Cancel = true;
    }

    private void ViewState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewState.SearchText) or nameof(MainViewState.SelectedCategory) or nameof(MainViewState.SelectedStatus))
        {
            RefreshVisibleRows();
        }

        if (e.PropertyName == nameof(MainViewState.FolderPath) && !string.IsNullOrWhiteSpace(_viewState.FolderPath))
        {
            AppendLog("Patch folder set to " + _viewState.FolderPath);
        }
    }

    private async Task RefreshCatalogAsync()
    {
        try
        {
            SetStatus("Refreshing");
            AppendLog("Refreshing live Project Reforged catalog.");
            _catalog = await _service.LoadCatalogAsync(CancellationToken.None);
            _metadataByUrl = await _service.LoadRemoteMetadataAsync(_catalog.Patches, CancellationToken.None);
            foreach (var patch in _catalog.Patches)
            {
                if (_metadataByUrl.TryGetValue(patch.DownloadUrl, out var metadata))
                {
                    patch.RemoteSizeBytes = metadata.ContentLength;
                }
            }

            _settings.LastCheckedUtc = DateTime.UtcNow.ToString("O");
            _viewState.LastCheckedText = DisplayFormatting.FormatLastChecked(_settings.LastCheckedUtc);
            await SaveSettingsAsync();
            BuildRowsFromCatalog();
            ApplyStatuses();
            AppendLog("Loaded " + _catalog.Patches.Count + " patch options from the live site.");
        }
        catch (Exception ex)
        {
            AppendLog("Refresh failed: " + ex.Message);
            System.Windows.MessageBox.Show(this, ex.Message, "Refresh Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetStatus("Ready");
        }
    }

    private async Task ReloadManifestAsync()
    {
        var manifest = await _service.LoadManifestAsync(_viewState.FolderPath, CancellationToken.None);
        _manifestByUrl = manifest.Downloads.ToDictionary(entry => entry.DownloadUrl, StringComparer.OrdinalIgnoreCase);
    }

    private async Task SaveManifestAsync()
    {
        var manifest = new PatchManifest
        {
            Downloads = _manifestByUrl.Values.OrderBy(entry => entry.FileName, StringComparer.OrdinalIgnoreCase).ToList()
        };

        await _service.SaveManifestAsync(_viewState.FolderPath, manifest, CancellationToken.None);
    }

    private void RefreshHistory()
    {
        _viewState.History.Clear();
        foreach (var entry in _settings.DownloadHistory
                     .OrderByDescending(item => item.TimestampUtc)
                     .Take(30))
        {
            var title = entry.PatchName;
            if (!string.IsNullOrWhiteSpace(entry.Variant))
            {
                title += " [" + entry.Variant + "]";
            }

            if (!string.IsNullOrWhiteSpace(entry.Version))
            {
                title += " " + entry.Version;
            }

            _viewState.History.Add(new HistoryListItem
            {
                TimestampText = DisplayFormatting.FormatLastChecked(entry.TimestampUtc),
                Title = title,
                StatusText = entry.Result,
                FolderPath = entry.FolderPath
            });
        }

        _viewState.HistoryHeaderText = _viewState.History.Count == 0
            ? "Recent downloads and repair actions appear here."
            : _viewState.History.Count == 1
                ? "1 recent download or repair is saved in history."
                : _viewState.History.Count + " recent downloads or repairs are saved in history.";
    }

    private void AddHistoryEntry(PatchRow row, string result)
    {
        _settings.DownloadHistory.Insert(0, new DownloadHistoryEntry
        {
            TimestampUtc = DateTime.UtcNow.ToString("O"),
            PatchName = row.Patch.Name,
            Variant = row.Patch.Variant,
            Version = row.Patch.LatestVersion,
            Result = result,
            FolderPath = _viewState.FolderPath,
            DownloadUrl = row.Patch.DownloadUrl
        });

        while (_settings.DownloadHistory.Count > 120)
        {
            _settings.DownloadHistory.RemoveAt(_settings.DownloadHistory.Count - 1);
        }

        RefreshHistory();
    }

    private void BuildRowsFromCatalog()
    {
        foreach (var row in _allRows)
        {
            row.PropertyChanged -= Row_PropertyChanged;
        }

        _allRows.Clear();
        foreach (var patch in _catalog.Patches)
        {
            var row = new PatchRow
            {
                Patch = patch
            };
            row.IsSelected = _settings.SelectedPatchUrls.Contains(patch.DownloadUrl, StringComparer.OrdinalIgnoreCase);
            row.RemoteSizeText = DisplayFormatting.FormatBytes(patch.RemoteSizeBytes > 0 ? patch.RemoteSizeBytes : null);
            row.RecommendationText = BuildRecommendationText(patch);
            row.RecommendationBrush = DisplayFormatting.RecommendationBrushFor(row.RecommendationText);
            row.GuidanceText = BuildGuidanceText(patch);
            row.PropertyChanged += Row_PropertyChanged;
            _allRows.Add(row);
        }

        _viewState.StableVersion = _catalog.Release.StableVersion;
        _viewState.SiteUpdateDate = _catalog.Release.ReleaseDate;
        RefreshVisibleRows();
        PatchGrid.SelectedItem = _viewState.VisibleRows.FirstOrDefault();
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingSelectionRules || e.PropertyName != nameof(PatchRow.IsSelected) || sender is not PatchRow row)
        {
            return;
        }

        if (row.IsSelected)
        {
            PromptForRequiredPatches(row, true);
        }

        EnforceSelectionRules(row);
        UpdateCounts();
    }

    private void EnforceSelectionRules(PatchRow? changedRow)
    {
        _isApplyingSelectionRules = true;
        try
        {
            if (changedRow is not null && changedRow.IsSelected)
            {
                if (changedRow.Patch.PatchId is "B" or "D" or "E")
                {
                    foreach (var linkedRow in _allRows.Where(row => row.Patch.PatchId is "B" or "D" or "E"))
                    {
                        linkedRow.IsSelected = true;
                    }
                }

                if (changedRow.Patch.PatchId is "L" or "U")
                {
                    foreach (var sibling in _allRows.Where(row => row.Patch.PatchId == changedRow.Patch.PatchId && row.Patch.DownloadUrl != changedRow.Patch.DownloadUrl))
                    {
                        sibling.IsSelected = false;
                    }
                }
            }
        }
        finally
        {
            _isApplyingSelectionRules = false;
        }

        UpdateCounts();
    }

    private void ApplyStatuses()
    {
        foreach (var row in _allRows)
        {
            ApplyStatus(row);
        }

        RefreshVisibleRows();
        UpdateCounts();
        UpdateChangeSummary();
    }

    private void ApplyStatus(PatchRow row)
    {
        var filePath = Path.Combine(_viewState.FolderPath, row.Patch.FileName);
        var exists = File.Exists(filePath);
        _manifestByUrl.TryGetValue(row.Patch.DownloadUrl, out var manifestEntry);
        row.RemoteSizeText = DisplayFormatting.FormatBytes(row.Patch.RemoteSizeBytes > 0 ? row.Patch.RemoteSizeBytes : null);

        if (!exists)
        {
            if (manifestEntry is not null)
            {
                row.Status = "Needs repair";
                row.StatusBrush = new SolidColorBrush(Color.FromRgb(253, 228, 228));
                row.InstalledVersion = string.IsNullOrWhiteSpace(manifestEntry.Version) ? "-" : manifestEntry.Version;
                row.DownloadedUtc = string.IsNullOrWhiteSpace(manifestEntry.DownloadedUtc) ? "-" : manifestEntry.DownloadedUtc;
                row.LocalSizeText = "-";
                row.UpdateDetails = "Tracked patch file is missing from the selected folder and should be repaired.";
                row.VerificationText = "Tracked file missing locally.";
                row.ReleaseNotesText = BuildReleaseNotesText(row.Patch);
                return;
            }

            if (HasSiblingVariantInstalled(row.Patch))
            {
                row.Status = "Other variant installed";
                row.StatusBrush = new SolidColorBrush(Color.FromRgb(250, 230, 230));
            }
            else
            {
                row.Status = "Not downloaded";
                row.StatusBrush = Brushes.White;
            }

            row.InstalledVersion = "-";
            row.DownloadedUtc = "-";
            row.LocalSizeText = "-";
            row.UpdateDetails = "Patch is not installed locally yet.";
            row.VerificationText = "No local file found.";
            row.ReleaseNotesText = BuildReleaseNotesText(row.Patch);
            return;
        }

        var localLength = new FileInfo(filePath).Length;

        if (manifestEntry is null)
        {
            row.Status = "Downloaded";
            row.InstalledVersion = "-";
            row.DownloadedUtc = "-";
            row.LocalSizeText = DisplayFormatting.FormatBytes(localLength);
            row.StatusBrush = new SolidColorBrush(Color.FromRgb(235, 238, 243));
            row.UpdateDetails = "Patch exists locally but no v2 tracking manifest entry was found for update comparison.";
            row.VerificationText = "Local file exists, but no tracking manifest entry was found.";
            row.ReleaseNotesText = BuildReleaseNotesText(row.Patch);
            return;
        }

        row.InstalledVersion = string.IsNullOrWhiteSpace(manifestEntry.Version) ? "-" : manifestEntry.Version;
        row.DownloadedUtc = string.IsNullOrWhiteSpace(manifestEntry.DownloadedUtc) ? "-" : manifestEntry.DownloadedUtc;
        row.LocalSizeText = DisplayFormatting.FormatBytes(localLength);
        row.UpdateDetails = BuildUpdateDetails(row.Patch, manifestEntry);
        row.VerificationText = BuildVerificationText(row.Patch, manifestEntry, localLength);
        row.ReleaseNotesText = BuildReleaseNotesText(row.Patch);

        if (NeedsRepair(row.Patch, manifestEntry, localLength))
        {
            row.Status = "Needs repair";
            row.StatusBrush = new SolidColorBrush(Color.FromRgb(253, 228, 228));
            return;
        }

        if (HasUpdate(row.Patch, manifestEntry))
        {
            row.Status = "Update available";
            row.StatusBrush = new SolidColorBrush(Color.FromRgb(255, 236, 208));
            return;
        }

        row.Status = "Up to date";
        row.StatusBrush = new SolidColorBrush(Color.FromRgb(226, 244, 233));
    }

    private bool NeedsRepair(PatchOption patch, PatchManifestEntry manifestEntry, long localLength)
    {
        if (manifestEntry.ContentLength > 0 && localLength != manifestEntry.ContentLength)
        {
            return true;
        }

        if (_metadataByUrl.TryGetValue(patch.DownloadUrl, out var metadata)
            && metadata.ContentLength > 0
            && localLength != metadata.ContentLength)
        {
            return true;
        }

        return false;
    }

    private bool HasUpdate(PatchOption patch, PatchManifestEntry manifestEntry)
    {
        return GetUpdateReasons(patch, manifestEntry).Count > 0;
    }

    private bool HasSiblingVariantInstalled(PatchOption patch)
    {
        return _allRows.Any(row =>
            row.Patch.PatchId == patch.PatchId
            && row.Patch.DownloadUrl != patch.DownloadUrl
            && _manifestByUrl.ContainsKey(row.Patch.DownloadUrl)
            && File.Exists(Path.Combine(_viewState.FolderPath, row.Patch.FileName)));
    }

    private void RefreshVisibleRows()
    {
        var filtered = _allRows.Where(MatchesFilters).OrderBy(row => row.Patch.PatchId).ThenBy(row => row.Variant).ToList();
        var selectedDownloadUrl = (PatchGrid.SelectedItem as PatchRow)?.Patch.DownloadUrl;
        _viewState.VisibleRows.Clear();
        foreach (var row in filtered)
        {
            _viewState.VisibleRows.Add(row);
        }

        if (filtered.Count == 0)
        {
            _viewState.DetailsText = "No patches match the current filters.";
            _viewState.CanOpenInstalledFile = false;
            _viewState.SelectedPatchPath = string.Empty;
        }
        else
        {
            var row = filtered.FirstOrDefault(candidate => string.Equals(candidate.Patch.DownloadUrl, selectedDownloadUrl, StringComparison.OrdinalIgnoreCase))
                ?? filtered[0];
            PatchGrid.SelectedItem = row;
            PatchGrid.ScrollIntoView(row);
            ApplySelectedPatch(row);
        }
    }

    private bool MatchesFilters(PatchRow row)
    {
        var search = _viewState.SearchText.Trim();
        var category = _viewState.SelectedCategory;
        var status = _viewState.SelectedStatus;

        var matchesSearch = string.IsNullOrWhiteSpace(search)
            || row.PatchName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Patch.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Variant.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Summary.Contains(search, StringComparison.OrdinalIgnoreCase);

        var matchesCategory = category == "All" || row.Category == category;
        var matchesStatus = status == "All" || row.Status == status;

        return matchesSearch && matchesCategory && matchesStatus;
    }

    private string BuildDetailsText(PatchRow row)
    {
        var builder = new StringBuilder();
        builder.AppendLine(row.Description);
        builder.AppendLine(row.Patch.Name + " [" + row.Patch.Variant + "]");
        builder.AppendLine();
        builder.AppendLine("OVERVIEW");
        builder.AppendLine("Category: " + row.Patch.Category);
        builder.AppendLine("Status: " + row.Status);
        builder.AppendLine("Suggested setup: " + row.RecommendationText);
        builder.AppendLine("Rules: " + row.Patch.Requirements);
        builder.AppendLine();
        builder.AppendLine("INSTALL GUIDANCE");
        builder.AppendLine(row.GuidanceText);
        builder.AppendLine();
        builder.AppendLine("VERSIONS");
        builder.AppendLine("Latest site version: " + row.Patch.LatestVersion);
        builder.AppendLine("Installed version: " + row.InstalledVersion);
        builder.AppendLine("Site update date: " + row.Patch.ReleaseDate);
        builder.AppendLine("Downloaded on: " + row.DownloadedUtc);
        builder.AppendLine("Live update flag: " + (row.Patch.SiteUpdated ? "Yes" : "No"));
        builder.AppendLine();
        builder.AppendLine("UPDATE CHECK");
        builder.AppendLine(row.UpdateDetails);
        builder.AppendLine();
        builder.AppendLine("VERIFICATION");
        builder.AppendLine(row.VerificationText);
        builder.AppendLine();
        builder.AppendLine("FILE SIZES");
        builder.AppendLine("Remote: " + row.RemoteSizeText);
        builder.AppendLine("Local: " + row.LocalSizeText);
        builder.AppendLine();
        builder.AppendLine("SUMMARY");
        builder.AppendLine(row.Patch.Summary);
        builder.AppendLine();
        builder.AppendLine("PATCH NOTES");
        builder.AppendLine(row.ReleaseNotesText);
        builder.AppendLine();
        builder.AppendLine("SOURCE");
        builder.AppendLine(row.Patch.DownloadUrl);
        builder.AppendLine();
        builder.AppendLine("DESTINATION");
        builder.AppendLine(Path.Combine(_viewState.FolderPath, row.Patch.FileName));
        return builder.ToString();
    }

    private void ApplySelectedPatch(PatchRow row)
    {
        _viewState.DetailsText = BuildDetailsText(row);
        _viewState.SelectedPatchGuidanceText = row.GuidanceText;
        _viewState.SelectedPatchReleaseNotesText = row.ReleaseNotesText;
        var filePath = GetPatchFilePath(row);
        _viewState.SelectedPatchPath = filePath;
        _viewState.CanOpenInstalledFile = File.Exists(filePath);
        _viewState.CanRepairSelection = row.Status is "Needs repair" or "Downloaded" or "Update available";
    }

    private void UpdateCounts()
    {
        _viewState.SelectedCount = _allRows.Count(row => row.IsSelected).ToString();
        var updates = _allRows.Count(row => row.Status == "Update available");
        _viewState.CanUpdateInstalled = updates > 0 && !_viewState.IsDownloadInProgress;
        _viewState.CanRepairSelection = !_viewState.IsDownloadInProgress
            && (_allRows.Any(row => row.IsSelected && row.Status is "Needs repair" or "Downloaded" or "Update available")
                || PatchGrid.SelectedItem is PatchRow selectedRow && selectedRow.Status is "Needs repair" or "Downloaded" or "Update available");
        _viewState.UpdateAlertText = updates == 0
            ? "No downloaded patches need updates"
            : updates + " downloaded patch(es) have live updates";
    }

    private void UpdateChangeSummary()
    {
        var updateRows = _allRows.Where(row => row.Status == "Update available").OrderBy(row => row.Patch.PatchId).ThenBy(row => row.Variant).ToList();
        _viewState.UpdateItems.Clear();
        if (updateRows.Count == 0)
        {
            _viewState.UpdateSummaryHeaderText = "Everything you have tracked locally is up to date with the current Project Reforged catalog.";
            _viewState.UpdateSummaryText = "Everything you have tracked locally is up to date with the current Project Reforged catalog.";
            return;
        }

        foreach (var row in updateRows)
        {
            _viewState.UpdateItems.Add(new UpdateSummaryItem
            {
                Title = row.Description + " (" + row.Patch.Name + " [" + row.Patch.Variant + "])",
                VersionChange = (string.IsNullOrWhiteSpace(row.InstalledVersion) || row.InstalledVersion == "-" ? "Installed version unknown" : row.InstalledVersion)
                    + " -> "
                    + row.Patch.LatestVersion,
                StatusText = row.UpdateDetails,
                DownloadUrl = row.Patch.DownloadUrl
            });
        }

        _viewState.UpdateSummaryHeaderText = updateRows.Count == 1
            ? "1 installed patch has a live update available."
            : updateRows.Count + " installed patches have live updates available.";
        _viewState.UpdateSummaryText = "Installed patches with live updates are listed here.";
    }

    private void SetStatus(string status)
    {
        _viewState.StatusText = status;
    }

    private void AppendLog(string message)
    {
        _viewState.Activity.Insert(0, new ActivityLogEntry
        {
            Timestamp = DateTime.Now.ToString("HH:mm:ss"),
            Message = message
        });

        while (_viewState.Activity.Count > 40)
        {
            _viewState.Activity.RemoveAt(_viewState.Activity.Count - 1);
        }
    }

    private async Task LoadSettingsAsync()
    {
        _settings = await SettingsStore.LoadAsync(CancellationToken.None);
        _viewState.FolderPath = string.IsNullOrWhiteSpace(_settings.FolderPath) ? _viewState.FolderPath : _settings.FolderPath;
        _viewState.LastCheckedText = DisplayFormatting.FormatLastChecked(_settings.LastCheckedUtc);
        _viewState.IsFirstRunHelpVisible = !_settings.HasDismissedFirstRunHelp;
        _viewState.FirstRunHelpText =
            "1. Choose your patch folder." + Environment.NewLine
            + "2. Use Check for Updates to refresh the live Project Reforged catalog." + Environment.NewLine
            + "3. Use Select Recommended for a quick baseline." + Environment.NewLine
            + "4. Use Update Installed whenever tracked patches show live updates." + Environment.NewLine
            + "5. Use Verify Files if you want a quick local integrity pass before downloading.";
        RefreshHistory();
        ApplyColumnWidths();
    }

    private async Task SaveSettingsAsync()
    {
        _settings.FolderPath = _viewState.FolderPath;
        _settings.SelectedPatchUrls = _allRows.Where(row => row.IsSelected).Select(row => row.Patch.DownloadUrl).ToList();
        _settings.ColumnWidths = PatchGrid.Columns
            .Where(column => !double.IsNaN(column.ActualWidth) && column.ActualWidth > 0)
            .ToDictionary(GetColumnKey, column => column.ActualWidth);
        await SettingsStore.SaveAsync(_settings, CancellationToken.None);
    }

    private void ApplyColumnWidths()
    {
        foreach (var column in PatchGrid.Columns)
        {
            var key = GetColumnKey(column);
            if (_settings.ColumnWidths.TryGetValue(key, out var width) && width > 20)
            {
                var clampedWidth = key == "Recommended" ? Math.Max(176, width) : width;
                column.Width = new DataGridLength(clampedWidth);
            }
        }

        if (RecommendedColumn.Width.DisplayValue < 176)
        {
            RecommendedColumn.Width = new DataGridLength(176);
        }
    }

    private void SelectRowByDownloadUrl(string downloadUrl)
    {
        var row = _allRows.FirstOrDefault(candidate => string.Equals(candidate.Patch.DownloadUrl, downloadUrl, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return;
        }

        if (!_viewState.VisibleRows.Contains(row))
        {
            _viewState.SearchText = string.Empty;
            _viewState.SelectedCategory = "All";
            _viewState.SelectedStatus = "All";
            RefreshVisibleRows();
        }

        PatchGrid.SelectedItem = row;
        PatchGrid.ScrollIntoView(row);
        PatchGrid.Focus();
        ApplySelectedPatch(row);
    }

    private string GetPatchFilePath(PatchRow row)
    {
        return Path.Combine(_viewState.FolderPath, row.Patch.FileName);
    }

    private static string GetColumnKey(DataGridColumn column)
    {
        return column.Header switch
        {
            TextBlock textBlock when !string.IsNullOrWhiteSpace(textBlock.Text) => textBlock.Text,
            string text when !string.IsNullOrWhiteSpace(text) => text,
            _ => column.Header?.ToString() ?? string.Empty
        };
    }

    private static string BuildRecommendationText(PatchOption patch)
    {
        if (patch.PatchId is "B" or "D" or "E")
        {
            return "World set";
        }

        if (patch.RequiredPatchIds.Count > 0)
        {
            return "Pair " + string.Join("+", patch.RequiredPatchIds);
        }

        return "-";
    }

    private static string BuildGuidanceText(PatchOption patch)
    {
        var lines = new List<string>();

        if (patch.LinkedPatchIds.Count > 0)
        {
            lines.Add("Linked install: PATCH-" + patch.PatchId + " is designed to travel with " + string.Join(", ", patch.LinkedPatchIds.Select(id => "PATCH-" + id)) + ".");
        }

        if (patch.RequiredPatchIds.Count > 0)
        {
            lines.Add("Recommended dependencies: " + string.Join(", ", patch.RequiredPatchIds.Select(id => "PATCH-" + id)) + ".");
        }

        if (patch.PatchId is "L" or "U")
        {
            lines.Add("Variant rule: only one " + patch.Name + " option should stay active at a time.");
        }

        if (lines.Count == 0)
        {
            lines.Add("No extra install guidance was published on the downloads page.");
        }

        return string.Join(" ", lines);
    }

    private static string BuildReleaseNotesText(PatchOption patch)
    {
        return patch.SiteUpdated
            ? patch.Name + " is listed in the current live Project Reforged update set. Open Patch Notes for the live downloads page."
            : "Open Patch Notes for the live Project Reforged downloads page.";
    }

    private string BuildVerificationText(PatchOption patch, PatchManifestEntry manifestEntry, long localLength)
    {
        var checks = new List<string>();

        if (manifestEntry.ContentLength > 0)
        {
            checks.Add(localLength == manifestEntry.ContentLength
                ? "Local size matches tracked manifest size."
                : "Local size differs from the tracked manifest size.");
        }

        if (_metadataByUrl.TryGetValue(patch.DownloadUrl, out var metadata) && metadata.ContentLength > 0)
        {
            checks.Add(localLength == metadata.ContentLength
                ? "Local size matches current live file size."
                : "Local size differs from the current live file size.");
        }

        if (checks.Count == 0)
        {
            checks.Add("Verification is limited because no size metadata is available yet.");
        }

        return string.Join(" ", checks);
    }

    private List<string> GetUpdateReasons(PatchOption patch, PatchManifestEntry manifestEntry)
    {
        var reasons = new List<string>();

        if (!string.IsNullOrWhiteSpace(patch.LatestVersion)
            && !string.Equals(patch.LatestVersion, "Unlisted", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(patch.LatestVersion, manifestEntry.Version, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Version changed from " + (string.IsNullOrWhiteSpace(manifestEntry.Version) ? "unknown" : manifestEntry.Version) + " to " + patch.LatestVersion + ".");
        }

        if (_metadataByUrl.TryGetValue(patch.DownloadUrl, out var metadata))
        {
            if (!string.IsNullOrWhiteSpace(metadata.ETag) && !string.Equals(metadata.ETag, manifestEntry.ETag, StringComparison.Ordinal))
            {
                reasons.Add("Server ETag changed.");
            }

            if (!string.IsNullOrWhiteSpace(metadata.LastModifiedUtc) && !string.Equals(metadata.LastModifiedUtc, manifestEntry.LastModifiedUtc, StringComparison.Ordinal))
            {
                reasons.Add("Remote modified timestamp changed.");
            }

            if (metadata.ContentLength > 0 && metadata.ContentLength != manifestEntry.ContentLength)
            {
                reasons.Add("Remote file size changed from " + DisplayFormatting.FormatBytes(manifestEntry.ContentLength) + " to " + DisplayFormatting.FormatBytes(metadata.ContentLength) + ".");
            }
        }

        return reasons;
    }

    private string BuildUpdateDetails(PatchOption patch, PatchManifestEntry manifestEntry)
    {
        var reasons = GetUpdateReasons(patch, manifestEntry);
        return reasons.Count == 0
            ? "Tracked file matches the current live metadata."
            : string.Join(" ", reasons);
    }

    private void PromptForRequiredPatches(PatchRow row, bool askUser)
    {
        if (row.Patch.RequiredPatchIds.Count == 0)
        {
            return;
        }

        var missingRows = _allRows
            .Where(candidate => row.Patch.RequiredPatchIds.Contains(candidate.Patch.PatchId, StringComparer.OrdinalIgnoreCase) && !candidate.IsSelected)
            .GroupBy(candidate => candidate.Patch.PatchId)
            .Select(group => group.First())
            .ToList();

        if (missingRows.Count == 0)
        {
            return;
        }

        if (askUser)
        {
            var patchList = string.Join(", ", missingRows.Select(item => item.Patch.Name));
            var result = System.Windows.MessageBox.Show(
                this,
                row.Patch.Name + " works best with required patches: " + patchList + "." + Environment.NewLine + Environment.NewLine + "Select them now?",
                "Select Required Patches",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        foreach (var requiredRow in missingRows)
        {
            requiredRow.IsSelected = true;
        }
    }

    private bool ConfirmDownloadFolder(IReadOnlyCollection<PatchRow> selected)
    {
        var folder = _viewState.FolderPath ?? string.Empty;
        var normalized = folder.ToLowerInvariant();
        var looksSafe = normalized.Contains(@"\data") || normalized.Contains("patch") || normalized.EndsWith(@"\data") || normalized.EndsWith(@"\patches");

        if (looksSafe)
        {
            return true;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            "The selected folder does not look like a WoW Data or patch folder." + Environment.NewLine + Environment.NewLine
            + "Downloads selected: " + selected.Count + Environment.NewLine
            + "Folder: " + folder + Environment.NewLine + Environment.NewLine
            + "Download here anyway?",
            "Confirm Download Folder",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private void VerifyLocalFiles(bool showSummary)
    {
        ApplyStatuses();

        var needsRepair = _allRows.Count(row => row.Status == "Needs repair");
        var upToDate = _allRows.Count(row => row.Status == "Up to date");
        var downloaded = _allRows.Count(row => row.Status == "Downloaded");

        var summary = "Verification finished. "
            + needsRepair + " need repair, "
            + upToDate + " are up to date, and "
            + downloaded + " local file(s) are untracked.";

        AppendLog(summary);
        if (showSummary)
        {
            System.Windows.MessageBox.Show(this, summary, "Verify Files", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private string GetSelectedPatchNotesUrl()
    {
        return "https://projectreforged.github.io/downloads/";
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private async Task DownloadRowsAsync(IReadOnlyList<PatchRow> selected, string completionLogMessage)
    {
        try
        {
            _downloadCancellation = new CancellationTokenSource();
            _viewState.IsDownloadInProgress = true;
            SetStatus("Downloading");
            _viewState.DownloadProgressMax = selected.Count;
            _viewState.DownloadProgressValue = 0;
            _viewState.DownloadProgressText = "Starting download queue...";
            UpdateCounts();

            for (var index = 0; index < selected.Count; index++)
            {
                var row = selected[index];
                var currentIndex = index + 1;
                _downloadCancellation.Token.ThrowIfCancellationRequested();
                var progress = new Progress<DownloadProgressInfo>(info =>
                {
                    var totalBytes = info.TotalBytes ?? row.Patch.RemoteSizeBytes;
                    var fraction = totalBytes > 0 ? Math.Min(1d, (double)info.BytesReceived / totalBytes) : 0d;
                    _viewState.DownloadProgressValue = (currentIndex - 1) + fraction;
                    _viewState.DownloadProgressText = "Downloading " + currentIndex + " of " + selected.Count + ": "
                        + row.Patch.Name + " [" + row.Patch.Variant + "] "
                        + DisplayFormatting.FormatBytes(info.BytesReceived) + " / " + DisplayFormatting.FormatBytes(totalBytes);
                });

                _viewState.DownloadProgressText = "Downloading " + currentIndex + " of " + selected.Count + ": " + row.Patch.Name + " [" + row.Patch.Variant + "]";
                AppendLog("Downloading " + row.Patch.Name + " [" + row.Patch.Variant + "].");
                await _service.DownloadPatchAsync(row.Patch, _viewState.FolderPath, progress, _downloadCancellation.Token);

                _metadataByUrl.TryGetValue(row.Patch.DownloadUrl, out var metadata);
                metadata ??= new RemoteFileMetadata();

                _manifestByUrl[row.Patch.DownloadUrl] = new PatchManifestEntry
                {
                    DownloadUrl = row.Patch.DownloadUrl,
                    PatchId = row.Patch.PatchId,
                    Variant = row.Patch.Variant,
                    FileName = row.Patch.FileName,
                    Version = row.Patch.LatestVersion,
                    ReleaseDate = row.Patch.ReleaseDate,
                    DownloadedUtc = DateTime.UtcNow.ToString("O"),
                    ETag = metadata.ETag,
                    LastModifiedUtc = metadata.LastModifiedUtc,
                    ContentLength = metadata.ContentLength
                };

                AddHistoryEntry(row, "Downloaded to " + _viewState.FolderPath);

                _viewState.DownloadProgressValue = currentIndex;
                _viewState.DownloadProgressText = "Finished " + currentIndex + " of " + selected.Count + ": " + row.Patch.Name;
            }

            await SaveManifestAsync();
            AppendLog(completionLogMessage);
            ApplyStatuses();
        }
        catch (OperationCanceledException)
        {
            AppendLog("Download queue was stopped by the user.");
            _viewState.DownloadProgressText = "Downloads stopped.";
            ApplyStatuses();
        }
        catch (Exception ex)
        {
            AppendLog("Download failed: " + ex.Message);
            _viewState.DownloadProgressText = "Download failed.";
            System.Windows.MessageBox.Show(this, ex.Message, "Download Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (_viewState.DownloadProgressText == "Downloads complete."
                || _viewState.DownloadProgressText == "Downloads stopped."
                || _viewState.DownloadProgressText == "Download failed.")
            {
                _viewState.DownloadProgressValue = Math.Min(_viewState.DownloadProgressValue, _viewState.DownloadProgressMax);
            }
            else if (selected.Count > 0)
            {
                _viewState.DownloadProgressValue = _viewState.DownloadProgressMax;
                _viewState.DownloadProgressText = "Downloads complete.";
            }

            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
            _viewState.IsDownloadInProgress = false;
            SetStatus("Ready");
            UpdateCounts();
            _ = SaveSettingsAsync();

            if (_closeAfterDownloadStop)
            {
                _closeAfterDownloadStop = false;
                _allowImmediateClose = true;
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
        }
    }

    private async Task CheckAppUpdatesAsync(bool userInitiated)
    {
        try
        {
            var currentVersion = GetCurrentAppVersion();

            if (string.IsNullOrWhiteSpace(_settings.AppReleaseApiUrl))
            {
                _viewState.IsAppUpdateBannerVisible = false;
                if (userInitiated)
                {
                    System.Windows.MessageBox.Show(
                        this,
                        "App update checks are ready, but no GitHub release API URL is configured yet." + Environment.NewLine + Environment.NewLine
                        + "Add AppReleaseApiUrl and AppReleasePageUrl to:" + Environment.NewLine
                        + SettingsStore.GetSettingsPath(),
                        "App Updates Not Configured",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            var latest = await _appUpdateService.LoadLatestReleaseAsync(_settings.AppReleaseApiUrl, _settings.AppReleasePageUrl, CancellationToken.None);
            _settings.LastAppUpdateCheckedUtc = DateTime.UtcNow.ToString("O");
            await SaveSettingsAsync();

            if (latest is null || string.IsNullOrWhiteSpace(latest.Version))
            {
                if (userInitiated)
                {
                    System.Windows.MessageBox.Show(this, "The latest app release could not be read from GitHub.", "App Update Check", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return;
            }

            if (AppUpdateService.IsNewerVersion(currentVersion, latest.Version)
                && !string.Equals(_settings.DismissedAppVersion, latest.Version, StringComparison.OrdinalIgnoreCase))
            {
                _viewState.AppUpdateBannerText = "A newer app version is available: v" + latest.Version + ". Use Check App Updates to open the latest release.";
                _viewState.IsAppUpdateBannerVisible = true;
                AppendLog("App update available: v" + latest.Version + ".");

                if (userInitiated && !string.IsNullOrWhiteSpace(latest.ReleasePageUrl))
                {
                    var result = System.Windows.MessageBox.Show(
                        this,
                        "A newer app version is available: v" + latest.Version + "." + Environment.NewLine + Environment.NewLine + "Open the release page now?",
                        "App Update Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        OpenUrl(latest.ReleasePageUrl);
                    }
                }

                return;
            }

            _viewState.IsAppUpdateBannerVisible = false;
            if (userInitiated)
            {
                System.Windows.MessageBox.Show(this, "This app is already on the latest known version.", "App Update Check", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            AppendLog("App update check failed: " + ex.Message);
            if (userInitiated)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "App Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private static string GetCurrentAppVersion()
    {
        return AppUpdateService.NormalizeVersion(typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "2.3.0");
    }
}
