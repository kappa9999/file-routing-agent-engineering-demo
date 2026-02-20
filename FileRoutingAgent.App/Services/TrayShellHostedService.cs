using System.Diagnostics;
using FileRoutingAgent.App.UI;
using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Infrastructure.Configuration;
using FileRoutingAgent.Infrastructure.Pipeline;
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
    IProjectStructureAuditor projectStructureAuditor,
    IDemoMirrorService demoMirrorService,
    IPathCanonicalizer pathCanonicalizer,
    SupportBundleService supportBundleService,
    IOptions<AgentRuntimeOptions> runtimeOptions,
    ILogger<TrayShellHostedService> logger) : IHostedService
{
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ToolStripMenuItem? _statusMenuItem;
    private bool _startupPromptShown;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await auditStore.InitializeAsync(cancellationToken);
        await BuildTrayAsync();
        await auditStore.WriteEventAsync(
            new Core.Domain.AuditEvent(DateTime.UtcNow, "tray_started"),
            cancellationToken);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);
            await PromptEasySetupIfNeededAsync();
        });
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

            var checkStructureItem = new Forms.ToolStripMenuItem("Run Project Structure Check");
            checkStructureItem.Click += async (_, _) => await RunProjectStructureCheckAsync();
            contextMenu.Items.Add(checkStructureItem);

            var refreshDemoMirrorItem = new Forms.ToolStripMenuItem("Build/Refresh Demo Mirror Now");
            refreshDemoMirrorItem.Click += async (_, _) => await BuildOrRefreshDemoMirrorAsync();
            contextMenu.Items.Add(refreshDemoMirrorItem);

            var openDemoMirrorItem = new Forms.ToolStripMenuItem("Open Demo Mirror Folder");
            openDemoMirrorItem.Click += (_, _) => OpenDemoMirrorFolder();
            contextMenu.Items.Add(openDemoMirrorItem);

            var toggleDemoModeItem = new Forms.ToolStripMenuItem("Demo Mode: Toggle On/Off");
            toggleDemoModeItem.Click += async (_, _) => await ToggleDemoModeAsync();
            contextMenu.Items.Add(toggleDemoModeItem);

            var diagnosticsItem = new Forms.ToolStripMenuItem("Diagnostics");
            diagnosticsItem.Click += async (_, _) => await ShowDiagnosticsAsync();
            contextMenu.Items.Add(diagnosticsItem);

            var exportSupportBundleItem = new Forms.ToolStripMenuItem("Export Support Bundle");
            exportSupportBundleItem.Click += async (_, _) => await ExportSupportBundleAsync();
            contextMenu.Items.Add(exportSupportBundleItem);

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

            var easySetupItem = new Forms.ToolStripMenuItem("Easy Setup Wizard (Recommended)");
            easySetupItem.Click += async (_, _) => await ShowEasySetupWizardAsync(forceShow: true);
            contextMenu.Items.Add(easySetupItem);

            var openGuideItem = new Forms.ToolStripMenuItem("Open Simple User Guide");
            openGuideItem.Click += (_, _) => OpenEngineerGuide();
            contextMenu.Items.Add(openGuideItem);

            var openAuditItem = new Forms.ToolStripMenuItem("Open Audit Store Folder");
            openAuditItem.Click += (_, _) => OpenPath(Path.GetDirectoryName(runtimeOptions.Value.DatabasePath) ?? runtimeOptions.Value.DatabasePath);
            contextMenu.Items.Add(openAuditItem);

            var openLogsItem = new Forms.ToolStripMenuItem("Open Log Folder");
            openLogsItem.Click += (_, _) => OpenPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FileRoutingAgent",
                "Logs"));
            contextMenu.Items.Add(openLogsItem);

            var exitItem = new Forms.ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.BalloonTipTitle = "File Routing Agent";
            _notifyIcon.BalloonTipText = "Monitoring started.";
            _notifyIcon.ShowBalloonTip(2500);

            var snapshot = snapshotAccessor.Snapshot;
            UpdateStatusMenuText(snapshot);
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
            var snapshot = snapshotAccessor.Snapshot;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                new DiagnosticsWindow(
                    rootStates,
                    scans,
                    events,
                    snapshot?.DemoMode ?? DemoModeState.Disabled,
                    snapshot?.UserPreferences.LastProjectStructureSummary).ShowDialog();
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
                var window = new ConfigurationEditorWindow(
                    snapshotAccessor,
                    runtimePolicyRefresher,
                    policyConfigManager,
                    projectStructureAuditor,
                    demoMirrorService,
                    pathCanonicalizer);
                window.ShowDialog();
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to open configuration editor.");
            OpenPath(snapshotAccessor.Snapshot?.PolicyPath ?? runtimeOptions.Value.PolicyPath);
        }
    }

    private async Task ExportSupportBundleAsync()
    {
        try
        {
            var result = await supportBundleService.CreateBundleAsync(CancellationToken.None);
            await auditStore.WriteEventAsync(
                new Core.Domain.AuditEvent(
                    DateTime.UtcNow,
                    "support_bundle_exported",
                    PayloadJson: Core.Domain.JsonPayload.Serialize(new
                    {
                        result.BundlePath,
                        includedFileCount = result.IncludedFiles.Count,
                        warningCount = result.Warnings.Count
                    })),
                CancellationToken.None);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var warningSummary = result.Warnings.Count == 0
                    ? string.Empty
                    : $"\n\nWarnings:\n- {string.Join("\n- ", result.Warnings.Take(5))}";

                var message =
                    $"Support bundle created.\n\n{result.BundlePath}\n\n" +
                    "Share this zip file for troubleshooting." +
                    warningSummary;

                System.Windows.MessageBox.Show(
                    message,
                    "Support Bundle Created",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);

                OpenPath(result.BundlePath);
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to export support bundle.");
            await auditStore.WriteEventAsync(
                new Core.Domain.AuditEvent(
                    DateTime.UtcNow,
                    "support_bundle_export_failed",
                    PayloadJson: Core.Domain.JsonPayload.Serialize(new { exception.Message })),
                CancellationToken.None);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Support bundle export failed.\n\n{exception.Message}",
                    "Support Bundle Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            });
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
            UpdateStatusMenuText(snapshot);
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
            UpdateStatusMenuText(snapshot);
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
            UpdateStatusMenuText(snapshot);
        });
    }

    private async Task RunProjectStructureCheckAsync()
    {
        try
        {
            var snapshot = await policyConfigManager.GetSnapshotAsync(CancellationToken.None);
            if (snapshot.Policy.Projects.Count == 0)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    System.Windows.MessageBox.Show(
                        "No projects are configured yet.",
                        "Project Structure Check",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                });
                return;
            }

            var reportLines = new List<string>();
            foreach (var project in snapshot.Policy.Projects)
            {
                var report = await projectStructureAuditor.CheckAsync(project, CancellationToken.None);
                reportLines.Add(
                    $"{report.ProjectId}: Exists={report.ExistsCount}, Missing={report.MissingCount}, OutsideRoot={report.OutsideRootCount}, AccessDenied={report.AccessDeniedCount}, Invalid={report.InvalidCount}");
            }

            snapshot.UserPreferences.LastProjectStructureSummary = string.Join(" | ", reportLines);
            await policyConfigManager.SaveUserPreferencesAsync(snapshot.UserPreferences, CancellationToken.None);

            await auditStore.WriteEventAsync(
                new Core.Domain.AuditEvent(
                    DateTime.UtcNow,
                    "project_structure_checked",
                    PayloadJson: Core.Domain.JsonPayload.Serialize(new
                    {
                        projectCount = snapshot.Policy.Projects.Count,
                        summary = snapshot.UserPreferences.LastProjectStructureSummary
                    })),
                CancellationToken.None);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Project structure check complete.\n\n{snapshot.UserPreferences.LastProjectStructureSummary}",
                    "Project Structure Check",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Project structure check failed.");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Project structure check failed.\n\n{exception.Message}",
                    "Project Structure Check",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            });
        }
    }

    private async Task BuildOrRefreshDemoMirrorAsync()
    {
        try
        {
            var snapshot = await policyConfigManager.GetSnapshotAsync(CancellationToken.None);
            if (snapshot.Policy.Projects.Count == 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(snapshot.UserPreferences.DemoMirrorFolderName))
            {
                snapshot.UserPreferences.DemoMirrorFolderName = "_FRA_Demo";
            }

            foreach (var project in snapshot.Policy.Projects)
            {
                var projectRoot = DemoModeStateFactory.ResolveProjectRoot(project, pathCanonicalizer);
                if (string.IsNullOrWhiteSpace(projectRoot))
                {
                    continue;
                }

                var mirrorRoot = pathCanonicalizer.Canonicalize(
                    Path.Combine(projectRoot, snapshot.UserPreferences.DemoMirrorFolderName));
                snapshot.UserPreferences.DemoMirrorRootsByProject[project.ProjectId] = mirrorRoot;
            }

            var demoState = DemoModeStateFactory.Resolve(snapshot, pathCanonicalizer) with { Enabled = true };

            var summaries = new List<string>();
            foreach (var project in snapshot.Policy.Projects)
            {
                var result = await demoMirrorService.RefreshAsync(project, demoState, CancellationToken.None);
                summaries.Add(
                    $"{result.ProjectId}: Created={result.CreatedCount}, Existing={result.ExistingCount}, Skipped={result.SkippedCount}, Errors={result.Errors.Count}");

                await auditStore.WriteEventAsync(
                    new Core.Domain.AuditEvent(
                        DateTime.UtcNow,
                        "demo_mirror_refreshed",
                        ProjectId: result.ProjectId,
                        PayloadJson: Core.Domain.JsonPayload.Serialize(new
                        {
                            result.LiveProjectRoot,
                            result.MirrorRoot,
                            result.CreatedCount,
                            result.ExistingCount,
                            result.SkippedCount,
                            errorCount = result.Errors.Count
                        })),
                    CancellationToken.None);
            }

            snapshot.UserPreferences.LastDemoMirrorRefreshUtc = DateTime.UtcNow;
            await policyConfigManager.SaveUserPreferencesAsync(snapshot.UserPreferences, CancellationToken.None);

            var refreshed = await runtimePolicyRefresher.RefreshAsync(CancellationToken.None);
            snapshotAccessor.Update(refreshed);
            UpdateStatusMenuText(refreshed);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Demo mirror refresh complete.\n\n{string.Join("\n", summaries)}",
                    "Demo Mirror",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Demo mirror refresh failed.");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Demo mirror refresh failed.\n\n{exception.Message}",
                    "Demo Mirror",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            });
        }
    }

    private async Task ToggleDemoModeAsync()
    {
        try
        {
            var snapshot = await policyConfigManager.GetSnapshotAsync(CancellationToken.None);
            if (snapshot.Policy.Projects.Count == 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(snapshot.UserPreferences.DemoMirrorFolderName))
            {
                snapshot.UserPreferences.DemoMirrorFolderName = "_FRA_Demo";
            }

            foreach (var project in snapshot.Policy.Projects)
            {
                var projectRoot = DemoModeStateFactory.ResolveProjectRoot(project, pathCanonicalizer);
                if (string.IsNullOrWhiteSpace(projectRoot))
                {
                    continue;
                }

                var mirrorRoot = pathCanonicalizer.Canonicalize(
                    Path.Combine(projectRoot, snapshot.UserPreferences.DemoMirrorFolderName));
                snapshot.UserPreferences.DemoMirrorRootsByProject[project.ProjectId] = mirrorRoot;
            }

            snapshot.UserPreferences.DemoModeEnabled = !snapshot.UserPreferences.DemoModeEnabled;
            await policyConfigManager.SaveUserPreferencesAsync(snapshot.UserPreferences, CancellationToken.None);

            await auditStore.WriteEventAsync(
                new Core.Domain.AuditEvent(
                    DateTime.UtcNow,
                    "demo_mode_toggled",
                    PayloadJson: Core.Domain.JsonPayload.Serialize(new
                    {
                        enabled = snapshot.UserPreferences.DemoModeEnabled
                    })),
                CancellationToken.None);

            var refreshed = await runtimePolicyRefresher.RefreshAsync(CancellationToken.None);
            snapshotAccessor.Update(refreshed);
            UpdateStatusMenuText(refreshed);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to toggle demo mode.");
        }
    }

    private void OpenDemoMirrorFolder()
    {
        var snapshot = snapshotAccessor.Snapshot;
        if (snapshot is null)
        {
            return;
        }

        var path = snapshot.DemoMode.ProjectMirrorRoots.Values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path))
        {
            if (snapshot.Policy.Projects.Count == 0)
            {
                return;
            }

            var projectRoot = DemoModeStateFactory.ResolveProjectRoot(snapshot.Policy.Projects[0], pathCanonicalizer);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }

            var folderName = string.IsNullOrWhiteSpace(snapshot.UserPreferences.DemoMirrorFolderName)
                ? "_FRA_Demo"
                : snapshot.UserPreferences.DemoMirrorFolderName;
            path = Path.Combine(projectRoot, folderName);
        }

        OpenPath(path);
    }

    private void UpdateStatusMenuText(RuntimeConfigSnapshot? snapshot)
    {
        if (_statusMenuItem is null)
        {
            return;
        }

        if (snapshot is null)
        {
            _statusMenuItem.Text = "Status: Unknown";
            return;
        }

        var paused = snapshot.UserPreferences.MonitoringPaused &&
                     (snapshot.UserPreferences.MonitoringPausedUntilUtc is null ||
                      snapshot.UserPreferences.MonitoringPausedUntilUtc > DateTime.UtcNow);
        if (paused)
        {
            _statusMenuItem.Text = snapshot.DemoMode.Enabled
                ? "Status: Monitoring Paused (Demo Mode)"
                : "Status: Monitoring Paused";
            return;
        }

        _statusMenuItem.Text = snapshot.DemoMode.Enabled
            ? "Status: Demo Mode (Mirror Only)"
            : "Status: Monitoring On";
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
                (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                 uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }

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

    private static void OpenEngineerGuide()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "docs", "ENGINEER_USER_GUIDE.md"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "docs", "ENGINEER_USER_GUIDE.md"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                OpenPath(candidate);
                return;
            }
        }

        OpenPath("https://github.com/kappa9999/file-routing-agent-engineering-demo/blob/main/docs/ENGINEER_USER_GUIDE.md");
    }

    private async Task PromptEasySetupIfNeededAsync()
    {
        if (_startupPromptShown)
        {
            return;
        }

        _startupPromptShown = true;

        try
        {
            var snapshot = snapshotAccessor.Snapshot ?? await runtimePolicyRefresher.RefreshAsync(CancellationToken.None);
            snapshotAccessor.Update(snapshot);

            if (snapshot.SafeModeEnabled)
            {
                return;
            }

            if (!NeedsEasySetup(snapshot))
            {
                return;
            }

            var shouldOpen = false;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var result = System.Windows.MessageBox.Show(
                    "File Routing Agent is not fully configured for this machine.\n\nOpen Easy Setup Wizard now?",
                    "Easy Setup Recommended",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                shouldOpen = result == System.Windows.MessageBoxResult.Yes;
            });

            if (!shouldOpen)
            {
                return;
            }

            await ShowEasySetupWizardAsync(forceShow: false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed during startup easy setup prompt.");
        }
    }

    private async Task ShowEasySetupWizardAsync(bool forceShow)
    {
        try
        {
            var snapshot = snapshotAccessor.Snapshot ?? await runtimePolicyRefresher.RefreshAsync(CancellationToken.None);
            snapshotAccessor.Update(snapshot);

            if (!forceShow && !NeedsEasySetup(snapshot))
            {
                return;
            }

            EasySetupInput? input = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var window = new EasySetupWizardWindow(
                    projectStructureAuditor,
                    demoMirrorService,
                    pathCanonicalizer);
                if (window.ShowDialog() == true)
                {
                    input = window.Result;
                }
            });

            if (input is null)
            {
                return;
            }

            await auditStore.WriteEventAsync(
                new Core.Domain.AuditEvent(
                    DateTime.UtcNow,
                    "easy_setup_submitted",
                    PayloadJson: Core.Domain.JsonPayload.Serialize(new
                    {
                        input.ProjectId,
                        input.ProjectName,
                        input.ProjectRoot,
                        input.WatchRoot,
                        input.WorkingCadRoot,
                        input.WorkingDesignRoot,
                        input.CadPublishPath,
                        input.PlotSetsPath,
                        input.ProgressPrintsPath,
                        input.ExhibitsPath,
                        input.CheckPrintsPath,
                        input.CleanSetsPath,
                        input.IncludeLocalWrongSaveFolders,
                        input.CreateStandardFolders,
                        input.EnableProjectWiseCommandProfile,
                        input.EnableDemoMode
                    })),
                CancellationToken.None);

            var updatedPolicy = BuildEasyPolicy(snapshot.Policy, input);
            FolderEnsureResult? folderEnsureResult = null;
            if (input.CreateStandardFolders)
            {
                folderEnsureResult = EnsureProjectFolders(updatedPolicy.Projects[0]);
            }

            var policyPath = snapshot.PolicyPath;
            var signaturePath = PolicyEditorUtility.ResolveSignaturePath(updatedPolicy, policyPath);

            await File.WriteAllTextAsync(policyPath, PolicyEditorUtility.SerializePolicy(updatedPolicy), CancellationToken.None);
            var signature = PolicyEditorUtility.ComputeSha256Hex(policyPath);
            await File.WriteAllTextAsync(signaturePath, signature, CancellationToken.None);

            var preferences = snapshot.UserPreferences;
            if (string.IsNullOrWhiteSpace(preferences.DemoMirrorFolderName))
            {
                preferences.DemoMirrorFolderName = "_FRA_Demo";
            }

            var demoMirrorRoot = pathCanonicalizer.Canonicalize(
                Path.Combine(input.ProjectRoot, preferences.DemoMirrorFolderName));
            preferences.DemoMirrorRootsByProject[input.ProjectId] = demoMirrorRoot;
            preferences.DemoModeEnabled = input.EnableDemoMode;
            await policyConfigManager.SaveUserPreferencesAsync(preferences, CancellationToken.None);

            var refreshed = await runtimePolicyRefresher.RefreshAsync(CancellationToken.None);
            snapshotAccessor.Update(refreshed);
            UpdateStatusMenuText(refreshed);

            await auditStore.WriteEventAsync(
                new Core.Domain.AuditEvent(
                    DateTime.UtcNow,
                    "easy_setup_applied",
                    PayloadJson: Core.Domain.JsonPayload.Serialize(new
                    {
                        input.ProjectId,
                        input.ProjectRoot,
                        input.WatchRoot,
                        input.WorkingCadRoot,
                        input.WorkingDesignRoot,
                        input.CadPublishPath,
                        input.PlotSetsPath,
                        input.ProgressPrintsPath,
                        input.ExhibitsPath,
                        input.CheckPrintsPath,
                        input.CleanSetsPath,
                        input.IncludeLocalWrongSaveFolders,
                        input.EnableProjectWiseCommandProfile,
                        input.EnableDemoMode,
                        demoMirrorRoot,
                        foldersCreated = folderEnsureResult?.Created.Count ?? 0,
                        foldersExisting = folderEnsureResult?.Existing.Count ?? 0,
                        folderCreateErrors = folderEnsureResult?.Failed
                    })),
                CancellationToken.None);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var folderWarningText = folderEnsureResult is { Failed.Count: > 0 }
                    ? $"\n\n{folderEnsureResult.Failed.Count} folders could not be created. Export a support bundle for review."
                    : string.Empty;

                System.Windows.MessageBox.Show(
                    $"Setup complete. Monitoring is now configured for this project.{folderWarningText}",
                    "Easy Setup Complete",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Easy setup wizard failed.");
            await auditStore.WriteEventAsync(
                new Core.Domain.AuditEvent(
                    DateTime.UtcNow,
                    "easy_setup_failed",
                    PayloadJson: Core.Domain.JsonPayload.Serialize(new { exception.Message })),
                CancellationToken.None);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Easy setup could not be completed.\n\n{exception.Message}",
                    "Easy Setup Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            });
        }
    }

    private static bool NeedsEasySetup(RuntimeConfigSnapshot snapshot)
    {
        if (snapshot.Policy.Projects.Count == 0)
        {
            return true;
        }

        var firstProject = snapshot.Policy.Projects[0];
        var hasPlaceholderId = firstProject.ProjectId.Equals("Project123", StringComparison.OrdinalIgnoreCase);
        var pathMatchersMissing = firstProject.PathMatchers.Count == 0 ||
                                  firstProject.PathMatchers.All(path => !Directory.Exists(path));

        return hasPlaceholderId && pathMatchersMissing;
    }

    private static FirmPolicy BuildEasyPolicy(FirmPolicy existingPolicy, EasySetupInput input)
    {
        var policy = PolicyEditorUtility.ClonePolicy(existingPolicy);
        var project = PolicyEditorUtility.BuildProjectTemplate(
            input.ProjectId,
            input.ProjectName,
            input.ProjectRoot,
            input.EnableProjectWiseCommandProfile);

        project.PathMatchers =
        [
            EnsureTrailingSlash(input.ProjectRoot)
        ];
        project.WorkingRoots =
        [
            input.WorkingCadRoot,
            input.WorkingDesignRoot
        ];
        project.OfficialDestinations.CadPublish = input.CadPublishPath;
        project.OfficialDestinations.PlotSets = input.PlotSetsPath;
        project.OfficialDestinations.PdfCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["progress_print"] = input.ProgressPrintsPath,
            ["exhibit"] = input.ExhibitsPath,
            ["check_print"] = input.CheckPrintsPath,
            ["clean_set"] = input.CleanSetsPath
        };

        policy.Projects.Clear();
        policy.Projects.Add(project);

        var candidateRoots = new List<string>();
        if (input.IncludeLocalWrongSaveFolders)
        {
            candidateRoots.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            candidateRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            candidateRoots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        }

        candidateRoots.AddRange(project.WorkingRoots);
        policy.Monitoring.CandidateRoots = candidateRoots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        policy.Monitoring.WatchRoots =
        [
            input.WatchRoot
        ];

        if (policy.ManagedExtensions.Count == 0)
        {
            policy.ManagedExtensions = [".dgn", ".dwg", ".dxf", ".pdf", ".pset"];
        }

        return policy;
    }

    private static string EnsureTrailingSlash(string path)
    {
        var normalized = path.Trim();
        return normalized.EndsWith('\\') ? normalized : $"{normalized}\\";
    }

    private static FolderEnsureResult EnsureProjectFolders(ProjectPolicy project)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in project.WorkingRoots)
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                folders.Add(root);
            }
        }

        if (!string.IsNullOrWhiteSpace(project.OfficialDestinations.CadPublish))
        {
            folders.Add(project.OfficialDestinations.CadPublish);
        }

        if (!string.IsNullOrWhiteSpace(project.OfficialDestinations.PlotSets))
        {
            folders.Add(project.OfficialDestinations.PlotSets);
        }

        foreach (var destination in project.OfficialDestinations.PdfCategories.Values)
        {
            if (!string.IsNullOrWhiteSpace(destination))
            {
                folders.Add(destination);
            }
        }

        var created = new List<string>();
        var existing = new List<string>();
        var failed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    existing.Add(folder);
                    continue;
                }

                Directory.CreateDirectory(folder);
                created.Add(folder);
            }
            catch (Exception exception)
            {
                failed[folder] = exception.Message;
            }
        }

        return new FolderEnsureResult(created, existing, failed);
    }

    private sealed record FolderEnsureResult(
        IReadOnlyList<string> Created,
        IReadOnlyList<string> Existing,
        IReadOnlyDictionary<string, string> Failed);
}
