using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;

namespace FileRoutingAgent.Core.Interfaces;

public interface IPolicyIntegrityGuard
{
    Task<PolicyIntegrityResult> VerifyAsync(string policyPath, PolicyIntegritySettings settings, CancellationToken cancellationToken);
}

public sealed record PolicyIntegrityResult(bool IsValid, string? Error);

public interface IPolicyConfigManager
{
    Task<RuntimeConfigSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
    Task SaveUserPreferencesAsync(UserPreferences preferences, CancellationToken cancellationToken);
}

public interface IPathCanonicalizer
{
    string Canonicalize(string path);
    bool PathStartsWith(string path, string root);
}

public interface ISourceWatcher : IAsyncDisposable
{
    Task StartAsync(Func<DetectionCandidate, ValueTask> onCandidate, Func<string, ValueTask>? onOverflow, CancellationToken cancellationToken);
}

public interface IReconciliationScanner
{
    Task<ScanRun> RunScanAsync(
        Func<DetectionCandidate, ValueTask> onCandidate,
        bool priority,
        CancellationToken cancellationToken);
}

public interface IEventNormalizer
{
    bool TryNormalize(
        DetectionCandidate candidate,
        TimeSpan cooldown,
        TimeSpan renameClusterWindow,
        out DetectionCandidate normalized);
}

public interface IAgentOriginSuppressor
{
    Task<bool> ShouldSuppressAsync(DetectionCandidate candidate, CancellationToken cancellationToken);
}

public interface IFileStabilityGate
{
    Task<StableFile?> WaitForStableAsync(DetectionCandidate candidate, StabilitySettings settings, CancellationToken cancellationToken);
}

public interface IFileClassifier
{
    ClassifiedFile? Classify(StableFile file, FirmPolicy policy);
}

public interface IProjectRegistry
{
    ProjectResolution? Resolve(ClassifiedFile file, FirmPolicy policy);
    bool IsInCandidateRoot(string path, FirmPolicy policy);
    bool IsInOfficialDestination(string path, ProjectPolicy project, IPathCanonicalizer canonicalizer);
}

public interface IUserPromptService
{
    Task<UserDecision> PromptForRoutingAsync(PromptContext context, CancellationToken cancellationToken);
    Task<ConflictChoice> PromptForConflictAsync(TransferPlan transferPlan, CancellationToken cancellationToken);
    Task<ConflictChoice> PromptForInvalidDestinationAsync(
        IReadOnlyList<ConflictValidationError> validationErrors,
        string sourcePath,
        string suggestedPath,
        CancellationToken cancellationToken);
}

public interface IPromptOrchestrator
{
    Task<UserDecision> RequestDecisionAsync(PromptContext context, CancellationToken cancellationToken);
}

public interface IRoutingRulesEngine
{
    RouteResult ResolveRoute(ClassifiedFile file, ProjectPolicy project, UserDecision decision, IPathCanonicalizer canonicalizer);
}

public interface IConflictResolver
{
    Task<ConflictPlan> BuildPlanAsync(
        ClassifiedFile file,
        RouteResult route,
        ProjectPolicy project,
        UserDecision decision,
        CancellationToken cancellationToken);
}

public interface ITransferEngine
{
    Task<TransferResult> ExecuteAsync(TransferPlan plan, CancellationToken cancellationToken);
}

public interface IAuditStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task WriteEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
    Task RecordScanRunAsync(ScanRun scanRun, CancellationToken cancellationToken);
    Task SavePendingItemAsync(PendingItem pendingItem, CancellationToken cancellationToken);
    Task UpdatePendingStatusAsync(long id, PendingStatus status, string? lastError, CancellationToken cancellationToken);
    Task<IReadOnlyList<PendingItem>> GetPendingItemsAsync(CancellationToken cancellationToken);
    Task SaveRecentOperationAsync(RecentOperation operation, CancellationToken cancellationToken);
    Task<bool> IsRecentOperationAsync(string path, long sizeBytes, DateTime lastWriteUtc, TimeSpan ttl, CancellationToken cancellationToken);
    Task CleanupRecentOperationsAsync(DateTime olderThanUtc, CancellationToken cancellationToken);
    Task<RootWatermark?> GetWatermarkAsync(string rootPath, CancellationToken cancellationToken);
    Task SaveWatermarkAsync(RootWatermark watermark, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditEventEntry>> GetRecentAuditEventsAsync(int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScanRunEntry>> GetRecentScanRunsAsync(int limit, CancellationToken cancellationToken);
}

public interface IRootAvailabilityTracker
{
    RootAvailabilityState GetState(string rootPath);
    void MarkAvailable(string rootPath, string? note = null);
    void MarkUnavailable(string rootPath, string? note = null);
    void MarkRecovering(string rootPath, string? note = null);
    IReadOnlyCollection<RootStateSnapshot> GetSnapshots();
}

public interface IConnectorHost
{
    Task<IReadOnlyDictionary<string, string>> PublishAsync(ConnectorPublishRequest request, CancellationToken cancellationToken);
}

public sealed record ConnectorPublishRequest(
    ClassifiedFile File,
    ProjectPolicy Project,
    string DestinationPath,
    ProposedAction Action);

public sealed record ConnectorPublishResult(
    bool Success,
    string Status,
    string? ExternalTransactionId = null,
    string? Error = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public interface IExternalSystemConnector
{
    string Name { get; }
    bool CanHandle(ProjectPolicy project);
    Task<ConnectorPublishResult> PublishAsync(ConnectorPublishRequest request, CancellationToken cancellationToken);
}

public interface IRuntimePolicyRefresher
{
    Task<RuntimeConfigSnapshot> RefreshAsync(CancellationToken cancellationToken);
}

public interface IManualDetectionIngress
{
    Task<bool> EnqueueAsync(DetectionCandidate candidate, CancellationToken cancellationToken);
}

public interface IScanScheduler
{
    void RequestPriorityScan(string rootPath);
    bool TryDequeuePriorityScan(out string? rootPath);
}

public interface IProjectStructureAuditor
{
    Task<ProjectStructureReport> CheckAsync(ProjectPolicy project, CancellationToken cancellationToken);
}

public interface IDemoMirrorService
{
    Task<DemoMirrorRefreshResult> RefreshAsync(ProjectPolicy project, DemoModeState state, CancellationToken cancellationToken);
}

public interface IDemoSnapshotTransformer
{
    RuntimeConfigSnapshot ApplyDemoOverlay(
        RuntimeConfigSnapshot baseSnapshot,
        DemoModeState state,
        IPathCanonicalizer canonicalizer);
}

public interface IDemoSafetyGuard
{
    bool IsAllowed(TransferPlan plan, DemoModeState state, out string reason);
    bool IsPathInMirrorScope(string path, DemoModeState state, IPathCanonicalizer canonicalizer);
}
