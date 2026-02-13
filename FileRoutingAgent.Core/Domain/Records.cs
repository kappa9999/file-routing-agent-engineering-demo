using System.Text.Json;

namespace FileRoutingAgent.Core.Domain;

public sealed record DetectionCandidate(
    string SourcePath,
    DetectionSource Source,
    DateTime DetectedAtUtc,
    long? SizeBytesHint = null,
    DateTime? LastWriteUtcHint = null,
    long? PendingItemId = null);

public sealed record StableFile(
    string SourcePath,
    long SizeBytes,
    DateTime LastWriteUtc,
    string Fingerprint);

public sealed record ClassifiedFile(
    StableFile File,
    FileCategory Category,
    string Extension);

public sealed record ProjectResolution(
    string ProjectId,
    string DisplayName,
    string? MatchedPrefix);

public sealed record UserDecision(
    ProposedAction Action,
    string? PdfCategoryKey = null,
    bool IgnoreOnce = false,
    TimeSpan? Snooze = null,
    bool AlwaysIgnoreFolder = false);

public sealed record RouteResult(
    string DestinationPath,
    bool IsOfficialDestination,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record ConflictValidationError(
    string Code,
    string Message);

public sealed record ConflictPlan(
    string FinalDestinationPath,
    bool HasConflict,
    string? ExistingPath,
    ConflictChoice Choice,
    IReadOnlyList<ConflictValidationError> ValidationErrors);

public sealed record TransferPlan(
    ClassifiedFile ClassifiedFile,
    ProjectResolution Project,
    UserDecision Decision,
    RouteResult Route,
    ConflictPlan Conflict,
    long? PendingItemId = null);

public sealed record TransferResult(
    bool Success,
    string SourcePath,
    string DestinationPath,
    ProposedAction Action,
    string? Error,
    int Attempts);

public sealed record PromptContext(
    ClassifiedFile ClassifiedFile,
    ProjectResolution Project,
    IReadOnlyList<string> PdfCategoryKeys,
    string? DefaultPdfCategory,
    ProposedAction DefaultAction,
    string? DestinationHint);

public sealed record PendingItem(
    long Id,
    string SourcePath,
    string Fingerprint,
    string? ProjectId,
    FileCategory Category,
    DateTime DetectedAtUtc,
    DetectionSource Source,
    PendingStatus Status,
    string? LastError);

public sealed record AuditEvent(
    DateTime AtUtc,
    string EventType,
    string? SourcePath = null,
    string? DestinationPath = null,
    string? Fingerprint = null,
    string? ProjectId = null,
    string? PayloadJson = null);

public sealed record AuditEventEntry(
    long Id,
    DateTime AtUtc,
    string EventType,
    string? SourcePath,
    string? DestinationPath,
    string? Fingerprint,
    string? ProjectId,
    string? PayloadJson);

public sealed record ScanRun(
    DateTime StartedUtc,
    DateTime FinishedUtc,
    string RootPath,
    int CandidatesFound,
    int Queued,
    int Skipped,
    int Errors);

public sealed record ScanRunEntry(
    long Id,
    DateTime StartedUtc,
    DateTime FinishedUtc,
    string RootPath,
    int CandidatesFound,
    int Queued,
    int Skipped,
    int Errors);

public sealed record RecentOperation(
    string DestinationPath,
    long SizeBytes,
    DateTime LastWriteUtc,
    DateTime RecordedAtUtc);

public sealed record RootWatermark(
    string RootPath,
    DateTime LastScanUtc,
    string? LastSeenPath);

public sealed record RootStateSnapshot(
    string RootPath,
    RootAvailabilityState State,
    DateTime UpdatedAtUtc,
    string? Note = null);

public sealed record PipelineItem(
    DetectionCandidate Candidate,
    ClassifiedFile ClassifiedFile,
    ProjectResolution ProjectResolution);

public static class JsonPayload
{
    public static string Serialize(object value) => JsonSerializer.Serialize(value);
}
