using System.Text.Json;
using ClosedXML.Excel;
using StorageAudit.Tool;

namespace FileRoutingAgent.Tests;

public sealed class StorageAuditToolTests
{
    [Fact]
    public void StorageAuditOptions_ParsesCliArguments()
    {
        var parsed = StorageAuditOptions.TryParse(
            [
                "--root", @"C:\Temp\AuditRoot",
                "--output-folder", @"C:\Temp\AuditOut",
                "--top-files-count", "15",
                "--candidate-min-size-mb", "100",
                "--candidate-min-age-days", "30",
                "--archive-project-age-days", "900",
                "--include-hidden", "false",
                "--skip-system-directories", "true"
            ],
            out var options,
            out var error,
            out var showHelp);

        Assert.True(parsed);
        Assert.False(showHelp);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(@"C:\Temp\AuditRoot", options!.RootPath);
        Assert.Equal(@"C:\Temp\AuditOut", options.OutputFolder);
        Assert.Equal(15, options.TopFilesCount);
        Assert.Equal(100, options.CandidateMinSizeMb);
        Assert.Equal(30, options.CandidateMinAgeDays);
        Assert.Equal(900, options.ArchiveProjectAgeDays);
        Assert.False(options.IncludeHidden);
        Assert.True(options.SkipSystemDirectories);
    }

    [Fact]
    public async Task StorageAuditRunner_GeneratesWorkbookAndExports()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "StorageAuditRunnerTests", Guid.NewGuid().ToString("N"), "PDrive");
        var outputFolder = Path.Combine(Path.GetTempPath(), "StorageAuditRunnerTestsOutput", Guid.NewGuid().ToString("N"));
        var projectAlpha = Path.Combine(testRoot, "ProjectAlpha", "Design");
        var projectBeta = Path.Combine(testRoot, "ProjectBeta", "CAD");
        Directory.CreateDirectory(projectAlpha);
        Directory.CreateDirectory(projectBeta);

        var fileOne = Path.Combine(projectAlpha, "old-large.pdf");
        var fileTwo = Path.Combine(projectBeta, "new-small.dgn");
        var fileThree = Path.Combine(testRoot, "root-note.txt");

        await File.WriteAllBytesAsync(fileOne, new byte[2048]);
        await File.WriteAllBytesAsync(fileTwo, new byte[128]);
        await File.WriteAllBytesAsync(fileThree, new byte[64]);

        File.SetLastWriteTimeUtc(fileOne, DateTime.UtcNow.AddDays(-800));
        File.SetLastWriteTimeUtc(fileTwo, DateTime.UtcNow.AddDays(-10));
        File.SetLastWriteTimeUtc(fileThree, DateTime.UtcNow.AddDays(-400));

        var runner = new StorageAuditRunner();
        var result = await runner.RunAsync(
            new StorageAuditOptions
            {
                RootPath = testRoot,
                OutputFolder = outputFolder,
                TopFilesCount = 10,
                CandidateMinSizeMb = 0,
                CandidateMinAgeDays = 0
            },
            CancellationToken.None);

        Assert.Equal(3, result.Summary.FilesScanned);
        Assert.True(File.Exists(result.Artifacts.WorkbookPath));
        Assert.True(File.Exists(result.Artifacts.LargestFilesCsvPath));
        Assert.True(File.Exists(result.Artifacts.ProjectRollupsCsvPath));
        Assert.True(File.Exists(result.Artifacts.CandidateReviewCsvPath));
        Assert.True(File.Exists(result.Artifacts.ExtensionSummaryCsvPath));
        Assert.True(File.Exists(result.Artifacts.RunManifestJsonPath));

        using var workbook = new XLWorkbook(result.Artifacts.WorkbookPath);
        Assert.NotNull(workbook.Worksheet("Summary"));
        Assert.NotNull(workbook.Worksheet("Largest Files"));
        Assert.NotNull(workbook.Worksheet("Candidate Review"));
        Assert.NotNull(workbook.Worksheet("Project Rollups"));
        Assert.NotNull(workbook.Worksheet("Extension Summary"));
        Assert.NotNull(workbook.Worksheet("Scan Issues"));

        using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(result.Artifacts.RunManifestJsonPath));
        Assert.Equal(3, manifestDocument.RootElement.GetProperty("summary").GetProperty("FilesScanned").GetInt64());
    }

    [Fact]
    public async Task StorageAuditRunner_RejectsOutputInsideScanRoot()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "StorageAuditRunnerSafety", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);

        var runner = new StorageAuditRunner();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(
                new StorageAuditOptions
                {
                    RootPath = testRoot,
                    OutputFolder = Path.Combine(testRoot, "AuditOutput")
                },
                CancellationToken.None));

        Assert.Contains("inside the scan root", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectWiseReconcileOptions_ParsesCliArguments()
    {
        var parsed = ProjectWiseReconcileOptions.TryParse(
            [
                "--p-root", @"P:\1000_Software",
                "--compare-root", @"C:\Users\akiswani\Documents\SoftwareFolderCompare",
                "--cutoff-date", "2025-04-01",
                "--output-folder", @"C:\Temp\PwReconcile"
            ],
            out var options,
            out var error,
            out var showHelp);

        Assert.True(parsed);
        Assert.False(showHelp);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(@"P:\1000_Software", options!.PDriveRootPath);
        Assert.Equal(@"C:\Users\akiswani\Documents\SoftwareFolderCompare", options.CompareRootPath);
        Assert.Equal(@"C:\Temp\PwReconcile", options.OutputFolder);
        Assert.Equal(new DateTime(2025, 4, 1), options.CutoffLocal.LocalDateTime.Date);
        Assert.Equal(2, options.MatchDateToleranceDays);
    }

    [Fact]
    public async Task ProjectWiseReconcileRunner_GeneratesWorkbookAndExports()
    {
        var root = Path.Combine(Path.GetTempPath(), "PwReconcileTests", Guid.NewGuid().ToString("N"));
        var pRoot = Path.Combine(root, "1000_Software");
        var compareRoot = Path.Combine(root, "SoftwareFolderCompare");
        var outputFolder = Path.Combine(Path.GetTempPath(), "PwReconcileTestsOutput", Guid.NewGuid().ToString("N"));
        var pSoftwareFolder = Path.Combine(pRoot, "Software");
        var compareFolderA = Path.Combine(compareRoot, "FolderA");
        var compareFolderB = Path.Combine(compareRoot, "FolderB");

        Directory.CreateDirectory(pSoftwareFolder);
        Directory.CreateDirectory(compareFolderA);
        Directory.CreateDirectory(compareFolderB);

        var cleanupFile = Path.Combine(pSoftwareFolder, "cleanup-match.txt");
        var changedFile = Path.Combine(pSoftwareFolder, "changed-match.txt");
        var missingFile = Path.Combine(pSoftwareFolder, "missing-only.txt");
        var ambiguousDuplicateFile = Path.Combine(pSoftwareFolder, "ambiguous.txt");
        var ambiguousDateMismatchFile = Path.Combine(pSoftwareFolder, "date-mismatch.txt");

        await File.WriteAllBytesAsync(cleanupFile, new byte[100]);
        await File.WriteAllBytesAsync(changedFile, new byte[200]);
        await File.WriteAllBytesAsync(missingFile, new byte[300]);
        await File.WriteAllBytesAsync(ambiguousDuplicateFile, new byte[400]);
        await File.WriteAllBytesAsync(ambiguousDateMismatchFile, new byte[500]);

        File.SetLastWriteTimeUtc(cleanupFile, new DateTime(2024, 12, 15, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(changedFile, new DateTime(2025, 4, 10, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(missingFile, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(ambiguousDuplicateFile, new DateTime(2024, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(ambiguousDateMismatchFile, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var cleanupCompare = Path.Combine(compareFolderA, "cleanup-match.txt");
        var changedCompare = Path.Combine(compareFolderA, "changed-match.txt");
        var ambiguousCompareA = Path.Combine(compareFolderA, "ambiguous.txt");
        var ambiguousCompareB = Path.Combine(compareFolderB, "ambiguous.txt");
        var dateMismatchCompare = Path.Combine(compareFolderA, "date-mismatch.txt");

        await File.WriteAllBytesAsync(cleanupCompare, new byte[100]);
        await File.WriteAllBytesAsync(changedCompare, new byte[200]);
        await File.WriteAllBytesAsync(ambiguousCompareA, new byte[400]);
        await File.WriteAllBytesAsync(ambiguousCompareB, new byte[400]);
        await File.WriteAllBytesAsync(dateMismatchCompare, new byte[500]);

        File.SetLastWriteTimeUtc(cleanupCompare, new DateTime(2024, 12, 16, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(changedCompare, new DateTime(2025, 4, 9, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(ambiguousCompareA, new DateTime(2024, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(ambiguousCompareB, new DateTime(2024, 11, 2, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(dateMismatchCompare, new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        var runner = new ProjectWiseReconcileRunner();
        var result = await runner.RunAsync(
            new ProjectWiseReconcileOptions
            {
                PDriveRootPath = pRoot,
                CompareRootPath = compareRoot,
                OutputFolder = outputFolder,
                CutoffLocal = new DateTimeOffset(new DateTime(2025, 4, 1, 0, 0, 0), TimeZoneInfo.Local.GetUtcOffset(new DateTime(2025, 4, 1)))
            },
            CancellationToken.None);

        Assert.Equal(5, result.Summary.PDriveFilesScanned);
        Assert.Equal(5, result.Summary.CompareFilesScanned);
        Assert.Equal(0, result.Summary.CompareIssueCount);
        Assert.Equal(1, result.Summary.MissingFromPwCount);
        Assert.Equal(1, result.Summary.ChangedAfterCutoffCount);
        Assert.Equal(1, result.Summary.CleanupReviewCount);
        Assert.Equal(2, result.Summary.AmbiguousCount);

        Assert.True(File.Exists(result.Artifacts.WorkbookPath));
        Assert.True(File.Exists(result.Artifacts.MissingFromPwCsvPath));
        Assert.True(File.Exists(result.Artifacts.ChangedAfterCutoffCsvPath));
        Assert.True(File.Exists(result.Artifacts.CleanupReviewCsvPath));
        Assert.True(File.Exists(result.Artifacts.AmbiguousMatchesCsvPath));
        Assert.True(File.Exists(result.Artifacts.ImportIssuesJsonPath));
        Assert.True(File.Exists(result.Artifacts.RunManifestJsonPath));

        using var workbook = new XLWorkbook(result.Artifacts.WorkbookPath);
        Assert.NotNull(workbook.Worksheet("Summary"));
        Assert.NotNull(workbook.Worksheet("Missing From Reference"));
        Assert.NotNull(workbook.Worksheet("Changed After Cutoff"));
        Assert.NotNull(workbook.Worksheet("Cleanup Review"));
        Assert.NotNull(workbook.Worksheet("Ambiguous Matches"));
        Assert.NotNull(workbook.Worksheet("Scan Issues"));

        using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(result.Artifacts.RunManifestJsonPath));
        var reportCounts = manifestDocument.RootElement.GetProperty("reportCounts");
        Assert.Equal(1, reportCounts.GetProperty("missingFromReference").GetInt32());
        Assert.Equal(1, reportCounts.GetProperty("changedAfterCutoff").GetInt32());
        Assert.Equal(1, reportCounts.GetProperty("cleanupReview").GetInt32());
        Assert.Equal(2, reportCounts.GetProperty("ambiguousMatches").GetInt32());
        Assert.Equal(0, reportCounts.GetProperty("scanIssues").GetInt32());
    }

    [Fact]
    public async Task ProjectWiseReconcileRunner_RejectsOutputInsideComparedRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "PwReconcileSafety", Guid.NewGuid().ToString("N"));
        var pRoot = Path.Combine(root, "1000_Software");
        var compareRoot = Path.Combine(root, "SoftwareFolderCompare");
        Directory.CreateDirectory(pRoot);
        Directory.CreateDirectory(compareRoot);

        var runner = new ProjectWiseReconcileRunner();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(
                new ProjectWiseReconcileOptions
                {
                    PDriveRootPath = pRoot,
                    CompareRootPath = compareRoot,
                    OutputFolder = Path.Combine(compareRoot, "PwReconcileOut")
                },
                CancellationToken.None));

        Assert.Contains("inside one of the compared folders", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
