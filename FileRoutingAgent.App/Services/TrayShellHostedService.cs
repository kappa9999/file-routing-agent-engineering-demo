using System.Diagnostics;
using FileRoutingAgent.App.UI;
using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;
using Forms = System.Windows.Forms;

namespace FileRoutingAgent.App.Services;

public sealed class TrayShellHostedService(
    IAuditStore auditStore,
    IPolicyConfigManager policyConfigManager,
    RuntimeConfigSnapshotAccessor snapshotAccessor,
    IRuntimePolicyRefresher runtimePolicyRefresher,
    IManualDetectionIngress manualDetectionIngress,
    IScanScheduler scanScheduler,
    IRootAvailabilityTracker rootAvailabilityTracker,
    IOptions<AgentRuntimeOptions> runtimeOptions,
    ILogger<TrayShellHostedService> logger) : IHostedService
{
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ToolStripMenuItem? _statusMenuItem;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await auditStore.InitializeAsync(cancellationToken);
        await BuildTrayAsync();
        await auditStore.WriteEventAsync(
            new Core.Domain.AuditEvent(DateTime.UtcNow, "tray_started"),
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }).Task;
    }

    private async Task BuildTrayAsync()
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "File Routing Agent",
                Visible = true
            };

            var contextMenu = new Forms.ContextMenuStrip();

            _statusMenuItem = new Forms.ToolStripMenuItem("Status: Monitoring On") { Enabled = false };
            contextMenu.Items.Add(_statusMenuItem);
            contextMenu.Items.Add(new Forms.ToolStripSeparator());

            var pendingItem = new Forms.ToolStripMenuItem("Review Pending Detections");
            pendingItem.Click += async (_, _) => await ShowPendingAsync();
            contextMenu.Items.Add(pendingItem);

            var recentActionsItem = new Forms.ToolStripMenuItem("Recent Actions");
            recentActionsItem.Click += async (_, _) => await ShowRecentActionsAsync();
            contextMenu.Items.Add(recentActionsItem);

            var runScanItem = new Forms.ToolStripMenuItem("Run Reconciliation Scan Now");
            runScanItem.Click += (_, _) => scanScheduler.RequestPriorityScan("manual");
            contextMenu.Items.Add(runScanItem);

            var diagnosticsItem = new Forms.ToolStripMenuItem("Diagnostics");
            diagnosticsItem.Click += async (_, _) => await ShowDiagnosticsAsync();
            contextMenu.Items.Add(diagnosticsItem);

            var pause15 = new Forms.ToolStripMenuItem("Pause Monitoring 15 Minutes");
            pause15.Click += async (_, _) => await PauseMonitoringAsync(TimeSpan.FromMinutes(15));
            contextMenu.Items.Add(pause15);

            var pause60 = new Forms.ToolStripMenuItem("Pause Monitoring 1 Hour");
            pause60.Click += async (_, _) => await PauseMonitoringAsync(TimeSpan.FromHours(1));
            contextMenu.Items.Add(pause60);

            var pauseUntilRestart = new Forms.ToolStripMenuItem("Pause Monitoring Until Restart");
            pauseUntilRestart.Click += async (_, _) => await PauseMonitoringUntilRestartAsync();
            contextMenu.Items.Add(pauseUntilRestart);

            var resumeItem = new Forms.ToolStripMenuItem("Resume Monitoring");
            resumeItem.Click += async (_, _) => await ResumeMonitoringAsync();
            contextMenu.Items.Add(resumeItem);

            contextMenu.Items.Add(new Forms.ToolStripSeparator());

            var openConfigItem = new Forms.ToolStripMenuItem("Open Configuration");
            openConfigItem.Click += async (_, _) => await ShowConfigurationEditorAsync();
            contextMenu.Items.Add(openConfigItem);

            var openAuditItem = new Forms.ToolStripMenuItem("Open Audit Store Folder");
            openAuditItem.Click += (_, _) => OpenPath(Path.GetDirectoryName(runtimeOptions.Value.DatabasePath) ?? runtimeOptions.Value.DatabasePath);
            contextMenu.Items.Add(openAuditItem);

            var exitItem = new Forms.ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.BalloonTipTitle = "File Routing Agent";
            _notifyIcon.BalloonTipText = "Monitoring started.";
            _notifyIcon.ShowBalloonTip(2500);

            var snapshot = snapshotAccessor.Snapshot;
            if (snapshot?.UserPreferences.MonitoringPaused == true &&
                (snapshot.UserPreferences.MonitoringPausedUntilUtc is null ||
                 snapshot.UserPreferences.MonitoringPausedUntilUtc > DateTime.UtcNow))
            {
                _statusMenuItem.Text = "Status: Monitoring Paused";
            }
        });
    }

    private async Task ShowPendingAsync()
    {
        try
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                new PendingDetectionsWindow(auditStore, manualDetectionIngress).ShowDialog();
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to load pending detections.");
        }
    }

    private async Task ShowRecentActionsAsync()
    {
        try
        {
            var events = await auditStore.GetRecentAuditEventsAsync(200, CancellationToken.None);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                new RecentActionsWindow(events).ShowDialog();
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to load recent actions.");
        }
    }

    private async Task ShowDiagnosticsAsync()
    {
        try
        {
            var scans = await auditStore.GetRecentScanRunsAsync(200, CancellationToken.None);
            var events = await auditStore.GetRecentAuditEventsAsync(400, CancellationToken.None);
            var rootStates = rootAvailabilityTracker.GetSnapshots();
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                new DiagnosticsWindow(rootStates, scans, events).ShowDialog();
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to show diagnostics.");
        }
    }

    private async Task ShowConfigurationEditorAsync()
    {
        try
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var window = new ConfigurationEditorWindow(snapshotAccessor, runtimePolicyRefresher);
                window.ShowDialog();
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to open configuration editor.");
            OpenPath(snapshotAccessor.Snapshot?.PolicyPath ?? runtimeOptions.Value.PolicyPath);
        }
    }

    private async Task PauseMonitoringAsync(TimeSpan duration)
    {
        var snapshot = snapshotAccessor.Snapshot;
        if (snapshot is null)
        {
            return;
        }

        snapshot.UserPreferences.MonitoringPaused = true;
        snapshot.UserPreferences.MonitoringPausedUntilUtc = DateTime.UtcNow.Add(duration);
        await policyConfigManager.SaveUserPreferencesAsync(snapshot.UserPreferences, CancellationToken.None);

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_statusMenuItem is not null)
            {
                _statusMenuItem.Text = "Status: Monitoring Paused";
            }
        });
    }

    private async Task ResumeMonitoringAsync()
    {
        var snapshot = snapshotAccessor.Snapshot;
        if (snapshot is null)
        {
            return;
        }

        snapshot.UserPreferences.MonitoringPaused = false;
        snapshot.UserPreferences.MonitoringPausedUntilUtc = null;
        await policyConfigManager.SaveUserPreferencesAsync(snapshot.UserPreferences, CancellationToken.None);

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_statusMenuItem is not null)
            {
                _statusMenuItem.Text = "Status: Monitoring On";
            }
        });
    }

    private async Task PauseMonitoringUntilRestartAsync()
    {
        var snapshot = snapshotAccessor.Snapshot;
        if (snapshot is null)
        {
            return;
        }

        snapshot.UserPreferences.MonitoringPaused = true;
        snapshot.UserPreferences.MonitoringPausedUntilUtc = null;
        await policyConfigManager.SaveUserPreferencesAsync(snapshot.UserPreferences, CancellationToken.None);

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_statusMenuItem is not null)
            {
                _statusMenuItem.Text = "Status: Monitoring Paused";
            }
        });
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }

            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch
        {
            // Ignore launch errors from shell open.
        }
    }
}
