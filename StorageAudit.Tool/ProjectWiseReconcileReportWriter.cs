using System.Text.Json;
using ClosedXML.Excel;

namespace StorageAudit.Tool;

public sealed class ProjectWiseReconcileReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly AuditLogWriter _logger;

    public ProjectWiseReconcileReportWriter(AuditLogWriter logger)
    {
        _logger = logger;
    }

    public async Task<ProjectWiseReconcileArtifacts> WriteAsync(
        ProjectWiseReconcileOptions options,
        ProjectWiseReconcileSummary summary,
        IReadOnlyList<ProjectWiseCompareRow> compareRows,
        IReadOnlyList<ProjectWiseImportIssueRow> issues,
        CancellationToken cancellationToken)
    {
        var outputFolder = options.OutputFolder ?? throw new InvalidOperationException("Output folder was not resolved.");
        var workbookPath = Path.Combine(outputFolder, "folder-compare-report.xlsx");
        var missingCsvPath = Path.Combine(outputFolder, "missing-from-reference.csv");
        var changedCsvPath = Path.Combine(outputFolder, "changed-after-cutoff.csv");
        var cleanupCsvPath = Path.Combine(outputFolder, "cleanup-review.csv");
        var ambiguousCsvPath = Path.Combine(outputFolder, "ambiguous-matches.csv");
        var issuesJsonPath = Path.Combine(outputFolder, "scan-issues.json");
        var runManifestJsonPath = Path.Combine(outputFolder, "run-manifest.json");
        var databasePath = Path.Combine(outputFolder, "projectwise-reconcile.db");
        var logPath = Path.Combine(outputFolder, "reconcile.log");

        var missingRows = compareRows.Where(row => row.MatchStatus == ProjectWiseMatchStatus.MissingFromPw).ToList();
        var changedRows = compareRows.Where(row => row.ChangedAfterCutoff).ToList();
        var cleanupRows = compareRows
            .Where(row => row.MatchStatus == ProjectWiseMatchStatus.MatchedInPw && !row.ChangedAfterCutoff)
            .Select(row => new ProjectWiseCleanupReviewRow(
                row.FullPath,
                row.ParentFolder,
                row.FileName,
                row.Extension,
                row.SizeBytes,
                row.SizeGb,
                row.LastWriteUtc,
                row.AgeDays,
                row.MatchingPwPaths,
                "Equivalent file appears to exist in the reference folder. Review before removing the primary copy."))
            .ToList();
        var ambiguousRows = compareRows.Where(row => row.MatchStatus == ProjectWiseMatchStatus.AmbiguousNeedsReview).ToList();

        await StorageAuditCsvWriter.WriteAsync(
            missingCsvPath,
            ["File Name", "Full Path", "Parent Folder", "Extension", "Size Bytes", "Size GB", "Last Write UTC", "Age Days"],
            missingRows,
            row => [row.FileName, row.FullPath, row.ParentFolder, row.Extension, row.SizeBytes, row.SizeGb, row.LastWriteUtc, row.AgeDays],
            cancellationToken);

        await StorageAuditCsvWriter.WriteAsync(
            changedCsvPath,
            ["File Name", "Full Path", "Parent Folder", "Extension", "Size Bytes", "Size GB", "Last Write UTC", "Age Days", "Match Status", "Matching Reference Paths"],
            changedRows,
            row => [row.FileName, row.FullPath, row.ParentFolder, row.Extension, row.SizeBytes, row.SizeGb, row.LastWriteUtc, row.AgeDays, row.MatchStatus.ToString(), row.MatchingPwPaths],
            cancellationToken);

        await StorageAuditCsvWriter.WriteAsync(
            cleanupCsvPath,
            ["File Name", "Full Path", "Parent Folder", "Extension", "Size Bytes", "Size GB", "Last Write UTC", "Age Days", "Matching Reference Paths", "Review Note"],
            cleanupRows,
            row => [row.FileName, row.FullPath, row.ParentFolder, row.Extension, row.SizeBytes, row.SizeGb, row.LastWriteUtc, row.AgeDays, row.MatchingPwPaths, row.ReviewNote],
            cancellationToken);

        await StorageAuditCsvWriter.WriteAsync(
            ambiguousCsvPath,
            ["File Name", "Full Path", "Parent Folder", "Extension", "Size Bytes", "Size GB", "Last Write UTC", "Age Days", "Exact Match Count", "Same Name + Size Count", "Candidate Reference Paths"],
            ambiguousRows,
            row => [row.FileName, row.FullPath, row.ParentFolder, row.Extension, row.SizeBytes, row.SizeGb, row.LastWriteUtc, row.AgeDays, row.ExactMatchCount, row.SameNameSizeCount, row.MatchingPwPaths],
            cancellationToken);

        await File.WriteAllTextAsync(issuesJsonPath, JsonSerializer.Serialize(issues, JsonOptions), cancellationToken);

        var manifest = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            mode = "compare-folders",
            options = new
            {
                options.PDriveRootPath,
                options.CompareRootPath,
                options.OutputFolder,
                cutoffLocal = options.CutoffLocal,
                cutoffUtc = options.CutoffUtc,
                options.MatchDateToleranceDays
            },
            summary,
            reportCounts = new
            {
                missingFromReference = missingRows.Count,
                changedAfterCutoff = changedRows.Count,
                cleanupReview = cleanupRows.Count,
                ambiguousMatches = ambiguousRows.Count,
                scanIssues = issues.Count
            }
        };
        await File.WriteAllTextAsync(runManifestJsonPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);

        WriteWorkbook(workbookPath, summary, missingRows, changedRows, cleanupRows, ambiguousRows, issues);
        _logger.Info($"ProjectWise reconcile workbook written: {workbookPath}");

        return new ProjectWiseReconcileArtifacts(
            databasePath,
            workbookPath,
            missingCsvPath,
            changedCsvPath,
            cleanupCsvPath,
            ambiguousCsvPath,
            issuesJsonPath,
            runManifestJsonPath,
            logPath);
    }

    private static void WriteWorkbook(
        string workbookPath,
        ProjectWiseReconcileSummary summary,
        IReadOnlyList<ProjectWiseCompareRow> missingRows,
        IReadOnlyList<ProjectWiseCompareRow> changedRows,
        IReadOnlyList<ProjectWiseCleanupReviewRow> cleanupRows,
        IReadOnlyList<ProjectWiseCompareRow> ambiguousRows,
        IReadOnlyList<ProjectWiseImportIssueRow> issues)
    {
        using var workbook = new XLWorkbook();

        WriteSummarySheet(workbook, summary);
        WriteMissingSheet(workbook, missingRows);
        WriteChangedSheet(workbook, changedRows);
        WriteCleanupSheet(workbook, cleanupRows);
        WriteAmbiguousSheet(workbook, ambiguousRows);
        WriteIssuesSheet(workbook, issues);

        workbook.SaveAs(workbookPath);
    }

    private static void WriteSummarySheet(XLWorkbook workbook, ProjectWiseReconcileSummary summary)
    {
        var sheet = workbook.Worksheets.Add("Summary");
        sheet.Cell(1, 1).Value = "Field";
        sheet.Cell(1, 2).Value = "Value";
        sheet.Cell(2, 1).Value = "Primary Folder";
        sheet.Cell(2, 2).Value = summary.PDriveRootPath;
        sheet.Cell(3, 1).Value = "Reference Folder";
        sheet.Cell(3, 2).Value = summary.CompareRootPath;
        sheet.Cell(4, 1).Value = "Output Folder";
        sheet.Cell(4, 2).Value = summary.OutputFolder;
        sheet.Cell(5, 1).Value = "Machine";
        sheet.Cell(5, 2).Value = summary.MachineName;
        sheet.Cell(6, 1).Value = "User";
        sheet.Cell(6, 2).Value = summary.UserName;
        sheet.Cell(7, 1).Value = "Cutoff Local";
        sheet.Cell(7, 2).Value = summary.CutoffLocal.LocalDateTime;
        sheet.Cell(8, 1).Value = "Cutoff UTC";
        sheet.Cell(8, 2).Value = summary.CutoffUtc.UtcDateTime;
        sheet.Cell(9, 1).Value = "Match Tolerance (Days)";
        sheet.Cell(9, 2).Value = summary.MatchDateToleranceDays;
        sheet.Cell(10, 1).Value = "Started UTC";
        sheet.Cell(10, 2).Value = summary.StartedUtc.UtcDateTime;
        sheet.Cell(11, 1).Value = "Finished UTC";
        sheet.Cell(11, 2).Value = summary.FinishedUtc.UtcDateTime;
        sheet.Cell(12, 1).Value = "Duration";
        sheet.Cell(12, 2).Value = summary.Duration.ToString();
        sheet.Cell(13, 1).Value = "Primary Directories Visited";
        sheet.Cell(13, 2).Value = summary.PDriveDirectoriesVisited;
        sheet.Cell(14, 1).Value = "Primary Directories Skipped";
        sheet.Cell(14, 2).Value = summary.PDriveDirectoriesSkipped;
        sheet.Cell(15, 1).Value = "Primary Files Scanned";
        sheet.Cell(15, 2).Value = summary.PDriveFilesScanned;
        sheet.Cell(16, 1).Value = "Primary Files Skipped";
        sheet.Cell(16, 2).Value = summary.PDriveFilesSkipped;
        sheet.Cell(17, 1).Value = "Primary Inaccessible Count";
        sheet.Cell(17, 2).Value = summary.PDriveInaccessibleCount;
        sheet.Cell(18, 1).Value = "Primary Total Size Bytes";
        sheet.Cell(18, 2).Value = summary.PDriveTotalBytes;
        sheet.Cell(19, 1).Value = "Compare Directories Visited";
        sheet.Cell(19, 2).Value = summary.CompareDirectoriesVisited;
        sheet.Cell(20, 1).Value = "Compare Directories Skipped";
        sheet.Cell(20, 2).Value = summary.CompareDirectoriesSkipped;
        sheet.Cell(21, 1).Value = "Compare Files Scanned";
        sheet.Cell(21, 2).Value = summary.CompareFilesScanned;
        sheet.Cell(22, 1).Value = "Compare Files Skipped";
        sheet.Cell(22, 2).Value = summary.CompareFilesSkipped;
        sheet.Cell(23, 1).Value = "Compare Inaccessible Count";
        sheet.Cell(23, 2).Value = summary.CompareInaccessibleCount;
        sheet.Cell(24, 1).Value = "Compare Total Size Bytes";
        sheet.Cell(24, 2).Value = summary.CompareTotalBytes;
        sheet.Cell(25, 1).Value = "Scan Issues";
        sheet.Cell(25, 2).Value = summary.CompareIssueCount;
        sheet.Cell(26, 1).Value = "Missing From Reference";
        sheet.Cell(26, 2).Value = summary.MissingFromPwCount;
        sheet.Cell(27, 1).Value = "Changed After Cutoff";
        sheet.Cell(27, 2).Value = summary.ChangedAfterCutoffCount;
        sheet.Cell(28, 1).Value = "Cleanup Review";
        sheet.Cell(28, 2).Value = summary.CleanupReviewCount;
        sheet.Cell(29, 1).Value = "Ambiguous Matches";
        sheet.Cell(29, 2).Value = summary.AmbiguousCount;
        FormatWorksheet(sheet);
    }

    private static void WriteMissingSheet(XLWorkbook workbook, IReadOnlyList<ProjectWiseCompareRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Missing From Reference");
        WriteHeaders(sheet, ["File Name", "Full Path", "Parent Folder", "Extension", "Size Bytes", "Size GB", "Last Write UTC", "Age Days"]);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            sheet.Cell(excelRow, 1).Value = row.FileName;
            sheet.Cell(excelRow, 2).Value = row.FullPath;
            sheet.Cell(excelRow, 3).Value = row.ParentFolder;
            sheet.Cell(excelRow, 4).Value = row.Extension;
            sheet.Cell(excelRow, 5).Value = row.SizeBytes;
            sheet.Cell(excelRow, 6).Value = row.SizeGb;
            sheet.Cell(excelRow, 7).Value = row.LastWriteUtc.UtcDateTime;
            sheet.Cell(excelRow, 8).Value = row.AgeDays;
        }

        FormatWorksheet(sheet);
    }

    private static void WriteChangedSheet(XLWorkbook workbook, IReadOnlyList<ProjectWiseCompareRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Changed After Cutoff");
        WriteHeaders(sheet, ["File Name", "Full Path", "Parent Folder", "Extension", "Size Bytes", "Size GB", "Last Write UTC", "Age Days", "Match Status", "Matching Reference Paths"]);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            sheet.Cell(excelRow, 1).Value = row.FileName;
            sheet.Cell(excelRow, 2).Value = row.FullPath;
            sheet.Cell(excelRow, 3).Value = row.ParentFolder;
            sheet.Cell(excelRow, 4).Value = row.Extension;
            sheet.Cell(excelRow, 5).Value = row.SizeBytes;
            sheet.Cell(excelRow, 6).Value = row.SizeGb;
            sheet.Cell(excelRow, 7).Value = row.LastWriteUtc.UtcDateTime;
            sheet.Cell(excelRow, 8).Value = row.AgeDays;
            sheet.Cell(excelRow, 9).Value = row.MatchStatus.ToString();
            sheet.Cell(excelRow, 10).Value = row.MatchingPwPaths;
        }

        FormatWorksheet(sheet);
    }

    private static void WriteCleanupSheet(XLWorkbook workbook, IReadOnlyList<ProjectWiseCleanupReviewRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Cleanup Review");
        WriteHeaders(sheet, ["File Name", "Full Path", "Parent Folder", "Extension", "Size Bytes", "Size GB", "Last Write UTC", "Age Days", "Matching Reference Paths", "Review Note"]);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            sheet.Cell(excelRow, 1).Value = row.FileName;
            sheet.Cell(excelRow, 2).Value = row.FullPath;
            sheet.Cell(excelRow, 3).Value = row.ParentFolder;
            sheet.Cell(excelRow, 4).Value = row.Extension;
            sheet.Cell(excelRow, 5).Value = row.SizeBytes;
            sheet.Cell(excelRow, 6).Value = row.SizeGb;
            sheet.Cell(excelRow, 7).Value = row.LastWriteUtc.UtcDateTime;
            sheet.Cell(excelRow, 8).Value = row.AgeDays;
            sheet.Cell(excelRow, 9).Value = row.MatchingPwPaths;
            sheet.Cell(excelRow, 10).Value = row.ReviewNote;
        }

        FormatWorksheet(sheet);
    }

    private static void WriteAmbiguousSheet(XLWorkbook workbook, IReadOnlyList<ProjectWiseCompareRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Ambiguous Matches");
        WriteHeaders(sheet, ["File Name", "Full Path", "Parent Folder", "Extension", "Size Bytes", "Size GB", "Last Write UTC", "Age Days", "Exact Match Count", "Same Name + Size Count", "Candidate Reference Paths"]);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            sheet.Cell(excelRow, 1).Value = row.FileName;
            sheet.Cell(excelRow, 2).Value = row.FullPath;
            sheet.Cell(excelRow, 3).Value = row.ParentFolder;
            sheet.Cell(excelRow, 4).Value = row.Extension;
            sheet.Cell(excelRow, 5).Value = row.SizeBytes;
            sheet.Cell(excelRow, 6).Value = row.SizeGb;
            sheet.Cell(excelRow, 7).Value = row.LastWriteUtc.UtcDateTime;
            sheet.Cell(excelRow, 8).Value = row.AgeDays;
            sheet.Cell(excelRow, 9).Value = row.ExactMatchCount;
            sheet.Cell(excelRow, 10).Value = row.SameNameSizeCount;
            sheet.Cell(excelRow, 11).Value = row.MatchingPwPaths;
        }

        FormatWorksheet(sheet);
    }

    private static void WriteIssuesSheet(XLWorkbook workbook, IReadOnlyList<ProjectWiseImportIssueRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Scan Issues");
        WriteHeaders(sheet, ["Source", "Error Type", "Message"]);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            sheet.Cell(excelRow, 1).Value = row.Source;
            sheet.Cell(excelRow, 2).Value = row.ErrorType;
            sheet.Cell(excelRow, 3).Value = row.Message;
        }

        FormatWorksheet(sheet);
    }

    private static void WriteHeaders(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            sheet.Cell(1, index + 1).Value = headers[index];
            sheet.Cell(1, index + 1).Style.Font.Bold = true;
        }
    }

    private static void FormatWorksheet(IXLWorksheet sheet)
    {
        sheet.SheetView.FreezeRows(1);
        sheet.RangeUsed()?.SetAutoFilter();
        sheet.Columns().AdjustToContents(12, 80);
    }
}
