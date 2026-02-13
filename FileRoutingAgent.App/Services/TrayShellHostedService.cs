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

            var easySetupItem = new Forms.ToolStripMenuItem("Easy Setup Wizard (Recommended)");
            easySetupItem.Click += async (_, _) => await ShowEasySetupWizardAsync(forceShow: true);
            contextMenu.Items.Add(easySetupItem);

            var openGuideItem = new Forms.ToolStripMenuItem("Open Simple User Guide");
            openGuideItem.Click += (_, _) => OpenEngineerGuide();
            contextMenu.Items.Add(openGuideItem);

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
                var window = new EasySetupWizardWindow();
                if (window.ShowDialog() == true)
                {
                    input = window.Result;
                }
            });

            if (input is null)
            {
                return;
            }

            var updatedPolicy = BuildEasyPolicy(snapshot.Policy, input);
            if (input.CreateStandardFolders)
            {
                EnsureProjectFolders(updatedPolicy.Projects[0]);
            }

            var policyPath = snapshot.PolicyPath;
            var signaturePath = PolicyEditorUtility.ResolveSignaturePath(updatedPolicy, policyPath);

            await File.WriteAllTextAsync(policyPath, PolicyEditorUtility.SerializePolicy(updatedPolicy), CancellationToken.None);
            var signature = PolicyEditorUtility.ComputeSha256Hex(policyPath);
            await File.WriteAllTextAsync(signaturePath, signature, CancellationToken.None);

            var refreshed = await runtimePolicyRefresher.RefreshAsync(CancellationToken.None);
            snapshotAccessor.Update(refreshed);

            await auditStore.WriteEventAsync(
                new Core.Domain.AuditEvent(
                    DateTime.UtcNow,
                    "easy_setup_applied",
                    PayloadJson: Core.Domain.JsonPayload.Serialize(new
                    {
                        input.ProjectId,
                        input.ProjectRoot,
                        input.IncludeLocalWrongSaveFolders,
                        input.EnableProjectWiseCommandProfile
                    })),
                CancellationToken.None);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.MessageBox.Show(
                    "Setup complete. Monitoring is now configured for this project.",
                    "Easy Setup Complete",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Easy setup wizard failed.");
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
            input.ProjectRoot.Trim()
        ];

        if (policy.ManagedExtensions.Count == 0)
        {
            policy.ManagedExtensions = [".dgn", ".dwg", ".dxf", ".pdf", ".pset"];
        }

        return policy;
    }

    private static void EnsureProjectFolders(ProjectPolicy project)
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

        foreach (var folder in folders)
        {
            Directory.CreateDirectory(folder);
        }
    }
}
