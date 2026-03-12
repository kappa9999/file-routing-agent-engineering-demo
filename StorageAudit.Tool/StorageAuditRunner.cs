namespace StorageAudit.Tool;

public sealed class StorageAuditRunner
{
    public async Task<StorageAuditRunResult> RunAsync(StorageAuditOptions rawOptions, CancellationToken cancellationToken)
    {
        var options = rawOptions.Normalize(DateTimeOffset.Now);
        ValidateOptions(options);

        Directory.CreateDirectory(options.OutputFolder!);

        var logPath = Path.Combine(options.OutputFolder!, "scan.log");
        using var logger = new AuditLogWriter(logPath);

        logger.Info($"Storage audit starting. Root={options.RootPath} Output={options.OutputFolder}");

        if (!Directory.Exists(options.RootPath))
        {
            throw new InvalidOperationException(
                $"Scan root '{options.RootPath}' was not found. If this is P:\\, run the tool inside the signed-in office user session or pass a UNC path with --root.");
        }

        if (IsSubPath(options.OutputFolder!, options.RootPath))
        {
            throw new InvalidOperationException(
                $"Output folder '{options.OutputFolder}' is inside the scan root '{options.RootPath}'. Choose a local workstation folder outside the share.");
        }

        var databasePath = Path.Combine(options.OutputFolder!, "audit-scan.db");
        await using var database = await StorageAuditDatabase.CreateAsync(databasePath, cancellationToken);

        var scanner = new StorageAuditScanner(database, logger);
        var summary = await scanner.ScanAsync(options, cancellationToken);
        logger.Info($"Scan complete. Files={summary.FilesScanned:N0} TotalBytes={summary.TotalBytes:N0}");

        await database.FinalizeForReportingAsync(cancellationToken);

        var reportWriter = new StorageAuditReportWriter(database, logger);
        var artifacts = await reportWriter.WriteAsync(options, summary, cancellationToken);

        logger.Info("Storage audit finished successfully.");
        return new StorageAuditRunResult(summary, artifacts);
    }

    private static void ValidateOptions(StorageAuditOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            throw new InvalidOperationException("Root path is required.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputFolder))
        {
            throw new InvalidOperationException("Output folder could not be resolved.");
        }

        if (options.TopFilesCount <= 0)
        {
            throw new InvalidOperationException("TopFilesCount must be greater than 0.");
        }

        if (options.CandidateMinSizeMb < 0 || options.CandidateMinAgeDays < 0 || options.ArchiveProjectAgeDays < 0)
        {
            throw new InvalidOperationException("Threshold values must be 0 or greater.");
        }
    }

    private static bool IsSubPath(string childPath, string rootPath)
    {
        var normalizedChild = NormalizePath(childPath);
        var normalizedRoot = NormalizePath(rootPath);
        return normalizedChild.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedChild.StartsWith($"{normalizedRoot}\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return path.TrimEnd('\\');
        }

        return Path.GetFullPath(path).TrimEnd('\\');
    }
}
