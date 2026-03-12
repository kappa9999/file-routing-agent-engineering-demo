using System.Text.Json;
using ClosedXML.Excel;

namespace StorageAudit.Tool;

public sealed class StorageAuditReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly StorageAuditDatabase _database;
    private readonly AuditLogWriter _logger;

    public StorageAuditReportWriter(StorageAuditDatabase database, AuditLogWriter logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task<StorageAuditArtifacts> WriteAsync(
        StorageAuditOptions options,
        ScanRunSummary summary,
        CancellationToken cancellationToken)
    {
        var outputFolder = options.OutputFolder ?? throw new InvalidOperationException("Output folder was not resolved.");
        var workbookPath = Path.Combine(outputFolder, "storage-audit-report.xlsx");
        var largestFilesCsvPath = Path.Combine(outputFolder, "largest-files.csv");
        var projectRollupsCsvPath = Path.Combine(outputFolder, "project-rollups.csv");
        var candidateReviewCsvPath = Path.Combine(outputFolder, "candidate-review.csv");
        var extensionSummaryCsvPath = Path.Combine(outputFolder, "extension-summary.csv");
        var scanIssuesJsonPath = Path.Combine(outputFolder, "scan-issues.json");
        var runManifestJsonPath = Path.Combine(outputFolder, "run-manifest.json");
        var databasePath = Path.Combine(outputFolder, "audit-scan.db");
        var logPath = Path.Combine(outputFolder, "scan.log");

        var largestFiles = await _database.GetLargestFilesAsync(options.TopFilesCount, cancellationToken);
        var projectRollups = await _database.GetProjectRollupsAsync(options.ArchiveProjectAgeDays, cancellationToken);
        var candidateReview = await _database.GetCandidateReviewRowsAsync(options.CandidateMinSizeBytes, options.CandidateMinAgeDays, cancellationToken);
        var extensionSummaries = await _database.GetExtensionSummariesAsync(cancellationToken);
        var issues = await _database.GetScanIssuesAsync(cancellationToken);

        await StorageAuditCsvWriter.WriteAsync(
            largestFilesCsvPath,
            ["Rank", "Project Bucket", "Full Path", "Parent Folder", "Extension", "Size Bytes", "Size GB", "Age Days", "Last Write UTC", "Created UTC"],
            largestFiles,
            row =>
            [
                row.Rank,
                row.ProjectBucket,
                row.FullPath,
                row.ParentFolder,
                row.Extension,
                row.SizeBytes,
                row.SizeGb,
                row.AgeDays,
                row.LastWriteUtc,
                row.CreatedUtc
            ],
            cancellationToken);

        await StorageAuditCsvWriter.WriteAsync(
            projectRollupsCsvPath,
            ["Project Bucket", "File Count", "Total Size Bytes", "Total Size GB", "Oldest Write UTC", "Newest Write UTC", "Bytes Older Than 1 Year", "Bytes Older Than 2 Years", "Bytes Older Than 5 Years", "Archive Review Flag"],
            projectRollups,
            row =>
            [
                row.ProjectBucket,
                row.FileCount,
                row.TotalSizeBytes,
                row.TotalSizeGb,
                row.OldestWriteUtc,
                row.NewestWriteUtc,
                row.BytesOlderThan1Year,
                row.BytesOlderThan2Years,
                row.BytesOlderThan5Years,
                row.ArchiveReviewFlag
            ],
            cancellationToken);

        await StorageAuditCsvWriter.WriteAsync(
            candidateReviewCsvPath,
            ["Project Bucket", "Full Path", "Parent Folder", "Extension", "Size Bytes", "Size GB", "Age Days", "Last Write UTC", "Disposition"],
            candidateReview,
            row =>
            [
                row.ProjectBucket,
                row.FullPath,
                row.ParentFolder,
                row.Extension,
                row.SizeBytes,
                row.SizeGb,
                row.AgeDays,
                row.LastWriteUtc,
                row.Disposition
            ],
            cancellationToken);

        await StorageAuditCsvWriter.WriteAsync(
            extensionSummaryCsvPath,
            ["Extension", "File Count", "Total Size Bytes", "Total Size GB", "Oldest Write UTC", "Newest Write UTC"],
            extensionSummaries,
            row =>
            [
                row.Extension,
                row.FileCount,
                row.TotalSizeBytes,
                row.TotalSizeGb,
                row.OldestWriteUtc,
                row.NewestWriteUtc
            ],
            cancellationToken);

        await File.WriteAllTextAsync(scanIssuesJsonPath, JsonSerializer.Serialize(issues, JsonOptions), cancellationToken);

        var manifest = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            options = new
            {
                options.RootPath,
                options.OutputFolder,
                options.TopFilesCount,
                options.CandidateMinSizeMb,
                options.CandidateMinAgeDays,
                options.ArchiveProjectAgeDays,
                options.IncludeHidden,
                options.SkipSystemDirectories,
                options.FollowReparsePoints
            },
            summary,
            reportCounts = new
            {
                largestFiles = largestFiles.Count,
                projectRollups = projectRollups.Count,
                candidateReview = candidateReview.Count,
                extensionSummaries = extensionSummaries.Count,
                issues = issues.Count
            }
        };
        await File.WriteAllTextAsync(runManifestJsonPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);

        WriteWorkbook(
            workbookPath,
            summary,
            largestFiles,
            candidateReview,
            projectRollups,
            extensionSummaries,
            issues);

        _logger.Info($"Workbook written: {workbookPath}");

        return new StorageAuditArtifacts(
            databasePath,
            workbookPath,
            largestFilesCsvPath,
            projectRollupsCsvPath,
            candidateReviewCsvPath,
            extensionSummaryCsvPath,
            scanIssuesJsonPath,
            runManifestJsonPath,
            logPath);
    }

    private static void WriteWorkbook(
        string workbookPath,
        ScanRunSummary summary,
        IReadOnlyList<LargestFileRow> largestFiles,
        IReadOnlyList<CandidateReviewRow> candidateReview,
        IReadOnlyList<ProjectRollupRow> projectRollups,
        IReadOnlyList<ExtensionSummaryRow> extensionSummaries,
        IReadOnlyList<ScanIssueRow> issues)
    {
        using var workbook = new XLWorkbook();

        var summarySheet = workbook.Worksheets.Add("Summary");
        summarySheet.Cell(1, 1).Value = "Field";
        summarySheet.Cell(1, 2).Value = "Value";
        summarySheet.Cell(2, 1).Value = "Root Scanned";
        summarySheet.Cell(2, 2).Value = summary.RootPath;
        summarySheet.Cell(3, 1).Value = "Output Folder";
        summarySheet.Cell(3, 2).Value = summary.OutputFolder;
        summarySheet.Cell(4, 1).Value = "Machine";
        summarySheet.Cell(4, 2).Value = summary.MachineName;
        summarySheet.Cell(5, 1).Value = "User";
        summarySheet.Cell(5, 2).Value = summary.UserName;
        summarySheet.Cell(6, 1).Value = "Started UTC";
        summarySheet.Cell(6, 2).Value = summary.StartedUtc.UtcDateTime;
        summarySheet.Cell(7, 1).Value = "Finished UTC";
        summarySheet.Cell(7, 2).Value = summary.FinishedUtc.UtcDateTime;
        summarySheet.Cell(8, 1).Value = "Duration";
        summarySheet.Cell(8, 2).Value = summary.Duration.ToString();
        summarySheet.Cell(9, 1).Value = "Directories Visited";
        summarySheet.Cell(9, 2).Value = summary.DirectoriesVisited;
        summarySheet.Cell(10, 1).Value = "Directories Skipped";
        summarySheet.Cell(10, 2).Value = summary.DirectoriesSkipped;
        summarySheet.Cell(11, 1).Value = "Files Scanned";
        summarySheet.Cell(11, 2).Value = summary.FilesScanned;
        summarySheet.Cell(12, 1).Value = "Files Skipped";
        summarySheet.Cell(12, 2).Value = summary.FilesSkipped;
        summarySheet.Cell(13, 1).Value = "Inaccessible Count";
        summarySheet.Cell(13, 2).Value = summary.InaccessibleCount;
        summarySheet.Cell(14, 1).Value = "Total Size Bytes";
        summarySheet.Cell(14, 2).Value = summary.TotalBytes;
        summarySheet.Cell(15, 1).Value = "Total Size GB";
        summarySheet.Cell(15, 2).Value = Math.Round(summary.TotalBytes / 1024d / 1024d / 1024d, 3);
        FormatWorksheet(summarySheet);

        WriteLargestFilesSheet(workbook, largestFiles);
        WriteCandidateReviewSheet(workbook, candidateReview);
        WriteProjectRollupsSheet(workbook, projectRollups);
        WriteExtensionSummarySheet(workbook, extensionSummaries);
        WriteIssuesSheet(workbook, issues);

        workbook.SaveAs(workbookPath);
    }

    private static void WriteLargestFilesSheet(XLWorkbook workbook, IReadOnlyList<LargestFileRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Largest Files");
        WriteHeaders(sheet, ["Rank", "Project Bucket", "Full Path", "Parent Folder", "Extension", "Size Bytes", "Size GB", "Age Days", "Last Write UTC", "Created UTC"]);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            sheet.Cell(excelRow, 1).Value = row.Rank;
            sheet.Cell(excelRow, 2).Value = row.ProjectBucket;
            sheet.Cell(excelRow, 3).Value = row.FullPath;
            sheet.Cell(excelRow, 4).Value = row.ParentFolder;
            sheet.Cell(excelRow, 5).Value = row.Extension;
            sheet.Cell(excelRow, 6).Value = row.SizeBytes;
            sheet.Cell(excelRow, 7).Value = row.SizeGb;
            sheet.Cell(excelRow, 8).Value = row.AgeDays;
            sheet.Cell(excelRow, 9).Value = row.LastWriteUtc.UtcDateTime;
            sheet.Cell(excelRow, 10).Value = row.CreatedUtc.UtcDateTime;
        }

        FormatWorksheet(sheet);
    }

    private static void WriteCandidateReviewSheet(XLWorkbook workbook, IReadOnlyList<CandidateReviewRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Candidate Review");
        WriteHeaders(sheet, ["Project Bucket", "Full Path", "Parent Folder", "Extension", "Size Bytes", "Size GB", "Age Days", "Last Write UTC", "Disposition"]);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            sheet.Cell(excelRow, 1).Value = row.ProjectBucket;
            sheet.Cell(excelRow, 2).Value = row.FullPath;
            sheet.Cell(excelRow, 3).Value = row.ParentFolder;
            sheet.Cell(excelRow, 4).Value = row.Extension;
            sheet.Cell(excelRow, 5).Value = row.SizeBytes;
            sheet.Cell(excelRow, 6).Value = row.SizeGb;
            sheet.Cell(excelRow, 7).Value = row.AgeDays;
            sheet.Cell(excelRow, 8).Value = row.LastWriteUtc.UtcDateTime;
            sheet.Cell(excelRow, 9).Value = row.Disposition;
        }

        FormatWorksheet(sheet);
    }

    private static void WriteProjectRollupsSheet(XLWorkbook workbook, IReadOnlyList<ProjectRollupRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Project Rollups");
        WriteHeaders(sheet, ["Project Bucket", "File Count", "Total Size Bytes", "Total Size GB", "Oldest Write UTC", "Newest Write UTC", "Bytes Older Than 1 Year", "Bytes Older Than 2 Years", "Bytes Older Than 5 Years", "Archive Review Flag"]);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            sheet.Cell(excelRow, 1).Value = row.ProjectBucket;
            sheet.Cell(excelRow, 2).Value = row.FileCount;
            sheet.Cell(excelRow, 3).Value = row.TotalSizeBytes;
            sheet.Cell(excelRow, 4).Value = row.TotalSizeGb;
            sheet.Cell(excelRow, 5).Value = row.OldestWriteUtc.UtcDateTime;
            sheet.Cell(excelRow, 6).Value = row.NewestWriteUtc.UtcDateTime;
            sheet.Cell(excelRow, 7).Value = row.BytesOlderThan1Year;
            sheet.Cell(excelRow, 8).Value = row.BytesOlderThan2Years;
            sheet.Cell(excelRow, 9).Value = row.BytesOlderThan5Years;
            sheet.Cell(excelRow, 10).Value = row.ArchiveReviewFlag;
        }

        FormatWorksheet(sheet);
    }

    private static void WriteExtensionSummarySheet(XLWorkbook workbook, IReadOnlyList<ExtensionSummaryRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Extension Summary");
        WriteHeaders(sheet, ["Extension", "File Count", "Total Size Bytes", "Total Size GB", "Oldest Write UTC", "Newest Write UTC"]);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            sheet.Cell(excelRow, 1).Value = row.Extension;
            sheet.Cell(excelRow, 2).Value = row.FileCount;
            sheet.Cell(excelRow, 3).Value = row.TotalSizeBytes;
            sheet.Cell(excelRow, 4).Value = row.TotalSizeGb;
            sheet.Cell(excelRow, 5).Value = row.OldestWriteUtc.UtcDateTime;
            sheet.Cell(excelRow, 6).Value = row.NewestWriteUtc.UtcDateTime;
        }

        FormatWorksheet(sheet);
    }

    private static void WriteIssuesSheet(XLWorkbook workbook, IReadOnlyList<ScanIssueRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Scan Issues");
        WriteHeaders(sheet, ["Path", "Error Type", "Message"]);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            sheet.Cell(excelRow, 1).Value = row.Path;
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
        }
    }

    private static void FormatWorksheet(IXLWorksheet sheet)
    {
        var range = sheet.RangeUsed();
        if (range is null)
        {
            return;
        }

        var headerRow = sheet.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
        sheet.SheetView.FreezeRows(1);
        range.SetAutoFilter();
        sheet.Columns().AdjustToContents();
        foreach (var column in sheet.ColumnsUsed())
        {
            if (column.Width > 80)
            {
                column.Width = 80;
            }
        }
    }
}
