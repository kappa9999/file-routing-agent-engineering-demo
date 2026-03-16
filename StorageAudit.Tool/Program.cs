using System.Text;

namespace StorageAudit.Tool;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length > 0 && string.Equals(args[0], "reconcile-pw", StringComparison.OrdinalIgnoreCase))
        {
            return await RunProjectWiseReconcileAsync(args[1..]);
        }

        return await RunStorageAuditAsync(args);
    }

    private static async Task<int> RunStorageAuditAsync(string[] args)
    {
        if (!StorageAuditOptions.TryParse(args, out var options, out var error, out var showHelp))
        {
            Console.Error.WriteLine(error);
            Console.WriteLine();
            Console.WriteLine(BuildCombinedUsage());
            return 1;
        }

        if (showHelp)
        {
            Console.WriteLine(BuildCombinedUsage());
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

    private static async Task<int> RunProjectWiseReconcileAsync(string[] args)
    {
        if (!ProjectWiseReconcileOptions.TryParse(args, out var options, out var error, out var showHelp))
        {
            Console.Error.WriteLine(error);
            Console.WriteLine();
            Console.WriteLine(ProjectWiseReconcileOptions.BuildUsage());
            return 1;
        }

        if (showHelp)
        {
            Console.WriteLine(ProjectWiseReconcileOptions.BuildUsage());
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
            var runner = new ProjectWiseReconcileRunner();
            var result = await runner.RunAsync(options!, cancellationTokenSource.Token);

            Console.WriteLine();
            Console.WriteLine("ProjectWise reconcile complete.");
            Console.WriteLine($"Workbook:        {result.Artifacts.WorkbookPath}");
            Console.WriteLine($"Missing CSV:     {result.Artifacts.MissingFromPwCsvPath}");
            Console.WriteLine($"Changed CSV:     {result.Artifacts.ChangedAfterCutoffCsvPath}");
            Console.WriteLine($"Cleanup CSV:     {result.Artifacts.CleanupReviewCsvPath}");
            Console.WriteLine($"Ambiguous CSV:   {result.Artifacts.AmbiguousMatchesCsvPath}");
            Console.WriteLine($"Manifest:        {result.Artifacts.RunManifestJsonPath}");
            Console.WriteLine($"Reconcile log:   {result.Artifacts.LogPath}");
            Console.WriteLine($"P files scanned:       {result.Summary.PDriveFilesScanned:N0}");
            Console.WriteLine($"Compare files scanned: {result.Summary.CompareFilesScanned:N0}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("ProjectWise reconcile cancelled.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ProjectWise reconcile failed: {exception.Message}");
            return 3;
        }
    }

    private static string BuildCombinedUsage()
    {
        return $"{StorageAuditOptions.BuildUsage()}{Environment.NewLine}{Environment.NewLine}{ProjectWiseReconcileOptions.BuildUsage()}";
    }
}
