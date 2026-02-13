using System.Diagnostics;
using System.IO;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;

namespace FileRoutingAgent.App.UI;

public partial class PendingDetectionsWindow : System.Windows.Window
{
    private readonly IAuditStore _auditStore;
    private readonly IManualDetectionIngress _manualDetectionIngress;

    public PendingDetectionsWindow(
        IAuditStore auditStore,
        IManualDetectionIngress manualDetectionIngress)
    {
        _auditStore = auditStore;
        _manualDetectionIngress = manualDetectionIngress;
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var pendingItems = await _auditStore.GetPendingItemsAsync(CancellationToken.None);
        SummaryText.Text = pendingItems.Count == 0
            ? "No pending detections."
            : $"Pending detections: {pendingItems.Count} (multi-select supported)";

        PendingGrid.ItemsSource = pendingItems
            .OrderByDescending(item => item.DetectedAtUtc)
            .Select(item => new PendingRow(item))
            .ToList();
    }

    private async void RefreshButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async void RetryButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var selectedRows = PendingGrid.SelectedItems.Cast<PendingRow>().ToList();
        if (selectedRows.Count == 0)
        {
            return;
        }

        var successCount = 0;
        var failedCount = 0;
        foreach (var row in selectedRows)
        {
            var enqueued = await _manualDetectionIngress.EnqueueAsync(
                new DetectionCandidate(
                    row.SourcePath,
                    row.Source,
                    DateTime.UtcNow,
                    PendingItemId: row.Id),
                CancellationToken.None);

            if (!enqueued)
            {
                failedCount++;
                continue;
            }

            successCount++;
            await _auditStore.UpdatePendingStatusAsync(
                row.Id,
                PendingStatus.Pending,
                "Manually retried from pending queue.",
                CancellationToken.None);
        }

        await RefreshAsync();
        System.Windows.MessageBox.Show(
            $"Manual retry queued for {successCount} item(s). Failed: {failedCount}.",
            "Pending Detections",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private async void DismissButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var selectedRows = PendingGrid.SelectedItems.Cast<PendingRow>().ToList();
        if (selectedRows.Count == 0)
        {
            return;
        }

        foreach (var row in selectedRows)
        {
            await _auditStore.UpdatePendingStatusAsync(
                row.Id,
                PendingStatus.Dismissed,
                "Dismissed from pending queue.",
                CancellationToken.None);
        }

        await RefreshAsync();
    }

    private void OpenSourceButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var selectedRow = PendingGrid.SelectedItem as PendingRow;
        if (selectedRow is null)
        {
            return;
        }

        try
        {
            if (File.Exists(selectedRow.SourcePath))
            {
                Process.Start(new ProcessStartInfo(selectedRow.SourcePath) { UseShellExecute = true });
                return;
            }

            var parent = Path.GetDirectoryName(selectedRow.SourcePath);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                Process.Start(new ProcessStartInfo(parent) { UseShellExecute = true });
                return;
            }

            System.Windows.MessageBox.Show(
                "Source path does not exist anymore.",
                "Open Source",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Unable to open source path.\n{exception.Message}",
                "Open Source",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void CloseButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }

    private sealed class PendingRow(PendingItem item)
    {
        public long Id { get; } = item.Id;
        public string DetectedAtUtc { get; } = item.DetectedAtUtc.ToString("u");
        public string? ProjectId { get; } = item.ProjectId;
        public string Category { get; } = item.Category.ToString();
        public string SourcePath { get; } = item.SourcePath;
        public string Status { get; } = item.Status.ToString();
        public DetectionSource Source { get; } = item.Source;
    }
}
