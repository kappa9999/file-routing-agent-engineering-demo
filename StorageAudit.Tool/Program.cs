using System.Text;

namespace StorageAudit.Tool;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (!StorageAuditOptions.TryParse(args, out var options, out var error, out var showHelp))
        {
            Console.Error.WriteLine(error);
            Console.WriteLine();
            Console.WriteLine(StorageAuditOptions.BuildUsage());
            return 1;
        }

        if (showHelp)
        {
            Console.WriteLine(StorageAuditOptions.BuildUsage());
            return 0;
        }

        using var cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        try
        {
            var runner = new StorageAuditRunner();
            var result = await runner.RunAsync(options!, cancellationTokenSource.Token);

            Console.WriteLine();
            Console.WriteLine("Storage audit complete.");
            Console.WriteLine($"Workbook:     {result.Artifacts.WorkbookPath}");
            Console.WriteLine($"Largest CSV:  {result.Artifacts.LargestFilesCsvPath}");
            Console.WriteLine($"Rollups CSV:  {result.Artifacts.ProjectRollupsCsvPath}");
            Console.WriteLine($"Candidates:   {result.Artifacts.CandidateReviewCsvPath}");
            Console.WriteLine($"Manifest:     {result.Artifacts.RunManifestJsonPath}");
            Console.WriteLine($"Scan log:     {result.Artifacts.LogPath}");
            Console.WriteLine($"Files scanned: {result.Summary.FilesScanned:N0}");
            Console.WriteLine($"Total bytes:   {result.Summary.TotalBytes:N0}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Storage audit cancelled.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Storage audit failed: {exception.Message}");
            return 3;
        }
    }
}
