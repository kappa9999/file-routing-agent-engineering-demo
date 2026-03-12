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
}
