namespace StorageAudit.Tool;

public sealed record FileInventoryRow(
    string FullPath,
    string ParentFolder,
    string ProjectBucket,
    string Extension,
    long SizeBytes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastWriteUtc,
    int AgeDays);

public sealed record LargestFileRow(
    int Rank,
    string ProjectBucket,
    string FullPath,
    string ParentFolder,
    string Extension,
    long SizeBytes,
    double SizeGb,
    int AgeDays,
    DateTimeOffset LastWriteUtc,
    DateTimeOffset CreatedUtc);

public sealed record ProjectRollupRow(
    string ProjectBucket,
    long FileCount,
    long TotalSizeBytes,
    double TotalSizeGb,
    DateTimeOffset OldestWriteUtc,
    DateTimeOffset NewestWriteUtc,
    long BytesOlderThan1Year,
    long BytesOlderThan2Years,
    long BytesOlderThan5Years,
    bool ArchiveReviewFlag);

public sealed record CandidateReviewRow(
    string ProjectBucket,
    string FullPath,
    string ParentFolder,
    string Extension,
    long SizeBytes,
    double SizeGb,
    int AgeDays,
    DateTimeOffset LastWriteUtc,
    string Disposition);

public sealed record ExtensionSummaryRow(
    string Extension,
    long FileCount,
    long TotalSizeBytes,
    double TotalSizeGb,
    DateTimeOffset OldestWriteUtc,
    DateTimeOffset NewestWriteUtc);

public sealed record ScanIssueRow(
    string Path,
    string ErrorType,
    string Message);

public sealed record ScanRunSummary(
    string RootPath,
    string OutputFolder,
    string MachineName,
    string UserName,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    long DirectoriesVisited,
    long DirectoriesSkipped,
    long FilesScanned,
    long FilesSkipped,
    long InaccessibleCount,
    long TotalBytes)
{
    public TimeSpan Duration => FinishedUtc - StartedUtc;
}

public sealed record StorageAuditArtifacts(
    string DatabasePath,
    string WorkbookPath,
    string LargestFilesCsvPath,
    string ProjectRollupsCsvPath,
    string CandidateReviewCsvPath,
    string ExtensionSummaryCsvPath,
    string ScanIssuesJsonPath,
    string RunManifestJsonPath,
    string LogPath);

public sealed record StorageAuditRunResult(
    ScanRunSummary Summary,
    StorageAuditArtifacts Artifacts);
