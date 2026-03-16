namespace StorageAudit.Tool;

public enum ProjectWiseMatchStatus
{
    MatchedInPw,
    MissingFromPw,
    AmbiguousNeedsReview
}

public sealed record ProjectWiseInventoryRow(
    string PwPath,
    string PwFolder,
    string FileName,
    string FileNameNormalized,
    long SizeBytes,
    DateTimeOffset LastWriteUtc,
    long LastWriteUnix);

public sealed record PDriveInventoryRow(
    string FullPath,
    string ParentFolder,
    string FileName,
    string FileNameNormalized,
    string Extension,
    long SizeBytes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastWriteUtc,
    long LastWriteUnix,
    int AgeDays);

public sealed record ProjectWiseCompareRow(
    ProjectWiseMatchStatus MatchStatus,
    string FullPath,
    string ParentFolder,
    string FileName,
    string Extension,
    long SizeBytes,
    double SizeGb,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastWriteUtc,
    int AgeDays,
    bool ChangedAfterCutoff,
    string MatchingPwPaths,
    int ExactMatchCount,
    int SameNameSizeCount);

public sealed record ProjectWiseCleanupReviewRow(
    string FullPath,
    string ParentFolder,
    string FileName,
    string Extension,
    long SizeBytes,
    double SizeGb,
    DateTimeOffset LastWriteUtc,
    int AgeDays,
    string MatchingPwPaths,
    string ReviewNote);

public sealed record ProjectWiseImportIssueRow(
    int? RowNumber,
    string Source,
    string ErrorType,
    string Message);

public sealed record ProjectWiseReconcileSummary(
    string PDriveRootPath,
    string CompareRootPath,
    string OutputFolder,
    string MachineName,
    string UserName,
    DateTimeOffset CutoffLocal,
    DateTimeOffset CutoffUtc,
    int MatchDateToleranceDays,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    long PDriveDirectoriesVisited,
    long PDriveDirectoriesSkipped,
    long PDriveFilesScanned,
    long PDriveFilesSkipped,
    long PDriveInaccessibleCount,
    long PDriveTotalBytes,
    long CompareDirectoriesVisited,
    long CompareDirectoriesSkipped,
    long CompareFilesScanned,
    long CompareFilesSkipped,
    long CompareInaccessibleCount,
    long CompareTotalBytes,
    long CompareIssueCount,
    long MissingFromPwCount,
    long ChangedAfterCutoffCount,
    long CleanupReviewCount,
    long AmbiguousCount)
{
    public TimeSpan Duration => FinishedUtc - StartedUtc;
}

public sealed record ProjectWiseReconcileArtifacts(
    string DatabasePath,
    string WorkbookPath,
    string MissingFromPwCsvPath,
    string ChangedAfterCutoffCsvPath,
    string CleanupReviewCsvPath,
    string AmbiguousMatchesCsvPath,
    string ImportIssuesJsonPath,
    string RunManifestJsonPath,
    string LogPath);

public sealed record ProjectWiseReconcileRunResult(
    ProjectWiseReconcileSummary Summary,
    ProjectWiseReconcileArtifacts Artifacts);

internal sealed record FolderScanStats(
    long DirectoriesVisited,
    long DirectoriesSkipped,
    long FilesScanned,
    long FilesSkipped,
    long InaccessibleCount,
    long TotalBytes);
