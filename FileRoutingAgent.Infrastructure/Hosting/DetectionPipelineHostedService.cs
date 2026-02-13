using System.Threading.Channels;
using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Core.Utilities;
using FileRoutingAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FileRoutingAgent.Infrastructure.Hosting;

public sealed class DetectionPipelineHostedService(
    IPolicyConfigManager configManager,
    RuntimeConfigSnapshotAccessor snapshotAccessor,
    IAuditStore auditStore,
    ISourceWatcher sourceWatcher,
    IReconciliationScanner scanner,
    IEventNormalizer normalizer,
    IAgentOriginSuppressor suppressor,
    IFileStabilityGate stabilityGate,
    IFileClassifier classifier,
    IProjectRegistry projectRegistry,
    IPromptOrchestrator promptOrchestrator,
    IRoutingRulesEngine routingRulesEngine,
    IConflictResolver conflictResolver,
    ITransferEngine transferEngine,
    IPathCanonicalizer pathCanonicalizer,
    IConnectorHost connectorHost,
    IScanScheduler scanScheduler,
    ILogger<DetectionPipelineHostedService> logger) : BackgroundService, IRuntimePolicyRefresher, IManualDetectionIngress
{
    private readonly Channel<DetectionCandidate> _detectionChannel = Channel.CreateBounded<DetectionCandidate>(new BoundedChannelOptions(5000)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    private readonly Channel<DetectionCandidate> _stabilityChannel = Channel.CreateBounded<DetectionCandidate>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    private readonly Channel<PipelineItem> _promptChannel = Channel.CreateBounded<PipelineItem>(new BoundedChannelOptions(300)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    private readonly Channel<TransferPlan> _transferChannel = Channel.CreateBounded<TransferPlan>(new BoundedChannelOptions(200)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Detection pipeline starting.");
        await auditStore.InitializeAsync(stoppingToken);

        var snapshot = await RefreshAsync(stoppingToken);
        await auditStore.WriteEventAsync(
            new AuditEvent(
                DateTime.UtcNow,
                "agent_startup",
                PayloadJson: JsonPayload.Serialize(new
                {
                    safeMode = snapshot.SafeModeEnabled,
                    reason = snapshot.SafeModeReason
                })),
            stoppingToken);

        await sourceWatcher.StartAsync(HandleSourceCandidateAsync, HandleWatcherOverflowAsync, stoppingToken);
        await RestorePendingAsync(stoppingToken);

        var detectionTask = Task.Run(() => DetectionStageAsync(stoppingToken), stoppingToken);
        var stabilityTask = Task.Run(() => StabilityStageAsync(stoppingToken), stoppingToken);
        var promptTask = Task.Run(() => PromptStageAsync(stoppingToken), stoppingToken);
        var transferTask = Task.Run(() => TransferStageAsync(stoppingToken), stoppingToken);
        var scannerTask = Task.Run(() => ScannerLoopAsync(stoppingToken), stoppingToken);

        await Task.WhenAll(detectionTask, stabilityTask, promptTask, transferTask, scannerTask);
        logger.LogInformation("Detection pipeline stopped.");
    }

    public async Task<RuntimeConfigSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        var snapshot = await configManager.GetSnapshotAsync(cancellationToken);
        snapshotAccessor.Update(snapshot);
        await auditStore.WriteEventAsync(
            new AuditEvent(
                DateTime.UtcNow,
                "policy_reloaded",
                PayloadJson: JsonPayload.Serialize(new
                {
                    safeMode = snapshot.SafeModeEnabled,
                    reason = snapshot.SafeModeReason,
                    snapshot.Policy.SchemaVersion
                })),
            cancellationToken);

        return snapshot;
    }

    public async Task<bool> EnqueueAsync(DetectionCandidate candidate, CancellationToken cancellationToken)
    {
        if (await _detectionChannel.Writer.WaitToWriteAsync(cancellationToken) &&
            _detectionChannel.Writer.TryWrite(candidate))
        {
            await auditStore.WriteEventAsync(
                new AuditEvent(
                    DateTime.UtcNow,
                    "manual_detection_enqueued",
                    SourcePath: candidate.SourcePath,
                    PayloadJson: JsonPayload.Serialize(new
                    {
                        source = candidate.Source.ToString(),
                        candidate.DetectedAtUtc
                    })),
                cancellationToken);
            return true;
        }

        await auditStore.WriteEventAsync(
            new AuditEvent(
                DateTime.UtcNow,
                "manual_detection_enqueue_failed",
                SourcePath: candidate.SourcePath),
            cancellationToken);
        return false;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _detectionChannel.Writer.TryComplete();
        _stabilityChannel.Writer.TryComplete();
        _promptChannel.Writer.TryComplete();
        _transferChannel.Writer.TryComplete();

        await sourceWatcher.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }

    private async ValueTask HandleSourceCandidateAsync(DetectionCandidate candidate)
    {
        await EnqueueWithBackpressureAsync(_detectionChannel.Writer, candidate, "detection_channel_drop", CancellationToken.None);
    }

    private async ValueTask HandleWatcherOverflowAsync(string rootPath)
    {
        scanScheduler.RequestPriorityScan(rootPath);
        await auditStore.WriteEventAsync(
            new AuditEvent(
                DateTime.UtcNow,
                "watcher_overflow",
                PayloadJson: JsonPayload.Serialize(new { rootPath })),
            CancellationToken.None);
    }

    private async Task DetectionStageAsync(CancellationToken cancellationToken)
    {
        while (await _detectionChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_detectionChannel.Reader.TryRead(out var candidate))
            {
                var snapshot = snapshotAccessor.Snapshot;
                if (snapshot is null || snapshot.SafeModeEnabled)
                {
                    continue;
                }

                if (!normalizer.TryNormalize(
                        candidate with { SourcePath = pathCanonicalizer.Canonicalize(candidate.SourcePath) },
                        TimeSpan.FromMinutes(Math.Max(1, snapshot.Policy.Monitoring.PromptCooldownMinutes)),
                        TimeSpan.FromSeconds(Math.Max(1, snapshot.Policy.Monitoring.RenameClusterWindowSeconds)),
                        out var normalized))
                {
                    continue;
                }

                if (await suppressor.ShouldSuppressAsync(normalized, cancellationToken))
                {
                    continue;
                }

                await EnqueueWithBackpressureAsync(_stabilityChannel.Writer, normalized, "stability_channel_drop", cancellationToken);
            }
        }
    }

    private async Task StabilityStageAsync(CancellationToken cancellationToken)
    {
        while (await _stabilityChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_stabilityChannel.Reader.TryRead(out var candidate))
            {
                var snapshot = snapshotAccessor.Snapshot;
                if (snapshot is null || snapshot.SafeModeEnabled)
                {
                    continue;
                }

                if (IsPaused(snapshot.UserPreferences))
                {
                    continue;
                }

                if (IsIgnoredOrSnoozed(candidate.SourcePath, snapshot.UserPreferences))
                {
                    continue;
                }

                var stableFile = await stabilityGate.WaitForStableAsync(candidate, snapshot.Policy.Stability, cancellationToken);
                if (stableFile is null)
                {
                    continue;
                }

                var classified = classifier.Classify(stableFile, snapshot.Policy);
                if (classified is null)
                {
                    logger.LogDebug("Skipped unmanaged or ignored file: {Path}", stableFile.SourcePath);
                    continue;
                }

                var resolution = projectRegistry.Resolve(classified, snapshot.Policy);
                if (resolution is null)
                {
                    logger.LogDebug("No project resolution for file: {Path}", stableFile.SourcePath);
                    continue;
                }

                if (!projectRegistry.IsInCandidateRoot(stableFile.SourcePath, snapshot.Policy))
                {
                    logger.LogDebug("File is outside candidate roots: {Path}", stableFile.SourcePath);
                    continue;
                }

                logger.LogDebug(
                    "Detection candidate accepted: {Path} [{Category}] project={ProjectId}",
                    stableFile.SourcePath,
                    classified.Category,
                    resolution.ProjectId);

                await EnqueueWithBackpressureAsync(
                    _promptChannel.Writer,
                    new PipelineItem(candidate, classified, resolution),
                    "prompt_channel_drop",
                    cancellationToken);
            }
        }
    }

    private async Task PromptStageAsync(CancellationToken cancellationToken)
    {
        while (await _promptChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_promptChannel.Reader.TryRead(out var pipelineItem))
            {
                var snapshot = snapshotAccessor.Snapshot;
                if (snapshot is null || snapshot.SafeModeEnabled)
                {
                    continue;
                }

                var project = snapshot.Policy.Projects.FirstOrDefault(p => p.ProjectId.Equals(pipelineItem.ProjectResolution.ProjectId, StringComparison.OrdinalIgnoreCase));
                if (project is null)
                {
                    continue;
                }

                if (projectRegistry.IsInOfficialDestination(
                        pipelineItem.ClassifiedFile.File.SourcePath,
                        project,
                        pathCanonicalizer) &&
                    project.Defaults.OfficialDestinationMode.Equals("monitor_no_prompt", StringComparison.OrdinalIgnoreCase))
                {
                    await auditStore.WriteEventAsync(
                        new AuditEvent(
                            DateTime.UtcNow,
                            "official_destination_detected",
                            SourcePath: pipelineItem.ClassifiedFile.File.SourcePath,
                            Fingerprint: pipelineItem.ClassifiedFile.File.Fingerprint,
                            ProjectId: project.ProjectId),
                        cancellationToken);

                    if (pipelineItem.Candidate.PendingItemId.HasValue)
                    {
                        await auditStore.UpdatePendingStatusAsync(
                            pipelineItem.Candidate.PendingItemId.Value,
                            PendingStatus.Done,
                            "No action required; source already in official destination.",
                            cancellationToken);
                    }
                    continue;
                }

                var defaultAction = project.ToDefaultAction(pipelineItem.ClassifiedFile.Category);
                var promptContext = new PromptContext(
                    pipelineItem.ClassifiedFile,
                    pipelineItem.ProjectResolution,
                    project.OfficialDestinations.PdfCategories.Keys.ToList(),
                    project.Defaults.DefaultPdfCategory,
                    defaultAction,
                    DestinationHint: null);

                var decision = await promptOrchestrator.RequestDecisionAsync(promptContext, cancellationToken);
                logger.LogInformation(
                    "User decision for {Path}: action={Action}, category={Category}, snooze={Snooze}",
                    pipelineItem.ClassifiedFile.File.SourcePath,
                    decision.Action,
                    decision.PdfCategoryKey ?? "(none)",
                    decision.Snooze);

                if (decision.AlwaysIgnoreFolder)
                {
                    var sourceDir = Path.GetDirectoryName(pipelineItem.ClassifiedFile.File.SourcePath);
                    if (!string.IsNullOrWhiteSpace(sourceDir))
                    {
                        snapshot.UserPreferences.IgnoredFolders.Add(sourceDir);
                        await configManager.SaveUserPreferencesAsync(snapshot.UserPreferences, cancellationToken);
                    }

                    if (pipelineItem.Candidate.PendingItemId.HasValue)
                    {
                        await auditStore.UpdatePendingStatusAsync(
                            pipelineItem.Candidate.PendingItemId.Value,
                            PendingStatus.Dismissed,
                            "Folder added to ignore list.",
                            cancellationToken);
                    }
                    continue;
                }

                if (decision.Snooze.HasValue)
                {
                    snapshot.UserPreferences.SnoozedPathsUtc[pipelineItem.ClassifiedFile.File.SourcePath] = DateTime.UtcNow.Add(decision.Snooze.Value);
                    await configManager.SaveUserPreferencesAsync(snapshot.UserPreferences, cancellationToken);
                    if (pipelineItem.Candidate.PendingItemId.HasValue)
                    {
                        await auditStore.UpdatePendingStatusAsync(
                            pipelineItem.Candidate.PendingItemId.Value,
                            PendingStatus.Pending,
                            "Snoozed by user",
                            cancellationToken);
                    }
                    else
                    {
                        await auditStore.SavePendingItemAsync(
                            new PendingItem(
                                0,
                                pipelineItem.ClassifiedFile.File.SourcePath,
                                pipelineItem.ClassifiedFile.File.Fingerprint,
                                pipelineItem.ProjectResolution.ProjectId,
                                pipelineItem.ClassifiedFile.Category,
                                DateTime.UtcNow,
                                pipelineItem.Candidate.Source,
                                PendingStatus.Pending,
                                "Snoozed by user"),
                            cancellationToken);
                    }
                    continue;
                }

                if (decision.IgnoreOnce || decision.Action is ProposedAction.Leave or ProposedAction.None)
                {
                    if (pipelineItem.Candidate.PendingItemId.HasValue)
                    {
                        await auditStore.UpdatePendingStatusAsync(
                            pipelineItem.Candidate.PendingItemId.Value,
                            PendingStatus.Dismissed,
                            "Dismissed by user decision.",
                            cancellationToken);
                    }
                    else
                    {
                        await auditStore.SavePendingItemAsync(
                            new PendingItem(
                                0,
                                pipelineItem.ClassifiedFile.File.SourcePath,
                                pipelineItem.ClassifiedFile.File.Fingerprint,
                                pipelineItem.ProjectResolution.ProjectId,
                                pipelineItem.ClassifiedFile.Category,
                                DateTime.UtcNow,
                                pipelineItem.Candidate.Source,
                                PendingStatus.Dismissed,
                                null),
                            cancellationToken);
                    }
                    continue;
                }

                var route = routingRulesEngine.ResolveRoute(pipelineItem.ClassifiedFile, project, decision, pathCanonicalizer);
                var conflict = await conflictResolver.BuildPlanAsync(
                    pipelineItem.ClassifiedFile,
                    route,
                    project,
                    decision,
                    cancellationToken);
                if (conflict.Choice == ConflictChoice.Cancel)
                {
                    await auditStore.WriteEventAsync(
                        new AuditEvent(
                            DateTime.UtcNow,
                            "conflict_cancelled",
                            SourcePath: pipelineItem.ClassifiedFile.File.SourcePath,
                            DestinationPath: route.DestinationPath,
                            Fingerprint: pipelineItem.ClassifiedFile.File.Fingerprint,
                            ProjectId: project.ProjectId),
                        cancellationToken);

                    if (pipelineItem.Candidate.PendingItemId.HasValue)
                    {
                        await auditStore.UpdatePendingStatusAsync(
                            pipelineItem.Candidate.PendingItemId.Value,
                            PendingStatus.Dismissed,
                            "Conflict decision cancelled by user.",
                            cancellationToken);
                    }
                    continue;
                }

                var transferPlan = new TransferPlan(
                    pipelineItem.ClassifiedFile,
                    pipelineItem.ProjectResolution,
                    decision,
                    route,
                    conflict,
                    pipelineItem.Candidate.PendingItemId);

                logger.LogDebug(
                    "Transfer queued: {Source} -> {Destination}",
                    transferPlan.ClassifiedFile.File.SourcePath,
                    transferPlan.Conflict.FinalDestinationPath);

                if (pipelineItem.Candidate.PendingItemId.HasValue)
                {
                    await auditStore.UpdatePendingStatusAsync(
                        pipelineItem.Candidate.PendingItemId.Value,
                        PendingStatus.Processing,
                        "Queued for transfer.",
                        cancellationToken);
                }

                await EnqueueWithBackpressureAsync(_transferChannel.Writer, transferPlan, "transfer_channel_drop", cancellationToken);
            }
        }
    }

    private async Task TransferStageAsync(CancellationToken cancellationToken)
    {
        while (await _transferChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_transferChannel.Reader.TryRead(out var plan))
            {
                var result = await transferEngine.ExecuteAsync(plan, cancellationToken);
                logger.LogInformation(
                    "Transfer result: success={Success} attempts={Attempts} source={Source} destination={Destination} error={Error}",
                    result.Success,
                    result.Attempts,
                    result.SourcePath,
                    result.DestinationPath,
                    result.Error);
                await auditStore.WriteEventAsync(
                    new AuditEvent(
                        DateTime.UtcNow,
                        result.Success ? "transfer_success" : "transfer_failure",
                        SourcePath: result.SourcePath,
                        DestinationPath: result.DestinationPath,
                        Fingerprint: plan.ClassifiedFile.File.Fingerprint,
                        ProjectId: plan.Project.ProjectId,
                        PayloadJson: JsonPayload.Serialize(new
                        {
                            result.Attempts,
                            result.Error,
                            action = result.Action.ToString()
                        })),
                    cancellationToken);

                if (plan.PendingItemId.HasValue)
                {
                    await auditStore.UpdatePendingStatusAsync(
                        plan.PendingItemId.Value,
                        result.Success ? PendingStatus.Done : PendingStatus.Error,
                        result.Error,
                        cancellationToken);
                }

                if (!result.Success)
                {
                    continue;
                }

                var info = new FileInfo(result.DestinationPath);
                if (info.Exists)
                {
                    await auditStore.SaveRecentOperationAsync(
                        new RecentOperation(
                            result.DestinationPath,
                            info.Length,
                            info.LastWriteTimeUtc,
                            DateTime.UtcNow),
                        cancellationToken);
                }

                var snapshot = snapshotAccessor.Snapshot;
                if (snapshot is null)
                {
                    continue;
                }

                var project = snapshot.Policy.Projects.FirstOrDefault(p => p.ProjectId.Equals(plan.Project.ProjectId, StringComparison.OrdinalIgnoreCase));
                if (project is null)
                {
                    continue;
                }

                var connectorMetadata = await connectorHost.PublishAsync(
                    new ConnectorPublishRequest(
                        plan.ClassifiedFile,
                        project,
                        result.DestinationPath,
                        plan.Decision.Action),
                    cancellationToken);
                await auditStore.WriteEventAsync(
                    new AuditEvent(
                        DateTime.UtcNow,
                        "connector_publish",
                        SourcePath: plan.ClassifiedFile.File.SourcePath,
                        DestinationPath: result.DestinationPath,
                        Fingerprint: plan.ClassifiedFile.File.Fingerprint,
                        ProjectId: plan.Project.ProjectId,
                        PayloadJson: JsonPayload.Serialize(connectorMetadata)),
                    cancellationToken);
            }
        }
    }

    private async Task ScannerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var snapshot = snapshotAccessor.Snapshot;
            if (snapshot is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                continue;
            }

            var intervalMinutes = Math.Max(1, snapshot.Policy.Monitoring.ReconciliationIntervalMinutes);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

            while (!cancellationToken.IsCancellationRequested)
            {
                await ExecuteScanAsync(priority: false, cancellationToken);

                while (scanScheduler.TryDequeuePriorityScan(out _))
                {
                    await ExecuteScanAsync(priority: true, cancellationToken);
                }

                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    break;
                }
            }
        }
    }

    private async Task ExecuteScanAsync(bool priority, CancellationToken cancellationToken)
    {
        var scanRun = await scanner.RunScanAsync(
            async candidate => await EnqueueWithBackpressureAsync(_detectionChannel.Writer, candidate, "scan_detection_drop", cancellationToken),
            priority,
            cancellationToken);

        await auditStore.RecordScanRunAsync(scanRun, cancellationToken);
    }

    private static bool IsPaused(UserPreferences preferences)
    {
        if (!preferences.MonitoringPaused)
        {
            return false;
        }

        if (preferences.MonitoringPausedUntilUtc is null)
        {
            return true;
        }

        return preferences.MonitoringPausedUntilUtc.Value > DateTime.UtcNow;
    }

    private static bool IsIgnoredOrSnoozed(string sourcePath, UserPreferences preferences)
    {
        if (preferences.SnoozedPathsUtc.TryGetValue(sourcePath, out var untilUtc) && untilUtc > DateTime.UtcNow)
        {
            return true;
        }

        var parent = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        return preferences.IgnoredFolders.Any(folder =>
            parent.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
    }

    private async Task RestorePendingAsync(CancellationToken cancellationToken)
    {
        var pending = await auditStore.GetPendingItemsAsync(cancellationToken);
        foreach (var item in pending)
        {
            await EnqueueWithBackpressureAsync(
                _detectionChannel.Writer,
                new DetectionCandidate(item.SourcePath, item.Source, DateTime.UtcNow, PendingItemId: item.Id),
                "pending_restore_drop",
                cancellationToken);
        }
    }

    private async Task EnqueueWithBackpressureAsync<T>(
        ChannelWriter<T> writer,
        T item,
        string dropEventType,
        CancellationToken cancellationToken)
    {
        if (await writer.WaitToWriteAsync(cancellationToken))
        {
            if (writer.TryWrite(item))
            {
                return;
            }
        }

        await auditStore.WriteEventAsync(
            new AuditEvent(
                DateTime.UtcNow,
                dropEventType,
                PayloadJson: JsonPayload.Serialize(new { itemType = typeof(T).Name })),
            cancellationToken);
    }
}
