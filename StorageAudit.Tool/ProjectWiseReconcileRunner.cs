namespace StorageAudit.Tool;

public sealed class ProjectWiseReconcileRunner
{
    public async Task<ProjectWiseReconcileRunResult> RunAsync(ProjectWiseReconcileOptions rawOptions, CancellationToken cancellationToken)
    {
        var options = rawOptions.Normalize(DateTimeOffset.Now);
        ValidateOptions(options);

        Directory.CreateDirectory(options.OutputFolder!);

        var logPath = Path.Combine(options.OutputFolder!, "reconcile.log");
        using var logger = new AuditLogWriter(logPath);

        logger.Info($"ProjectWise reconcile starting. PRoot={options.PDriveRootPath} CompareRoot={options.CompareRootPath} Output={options.OutputFolder}");

        if (!Directory.Exists(options.PDriveRootPath))
        {
            throw new InvalidOperationException(
                $"P-drive root '{options.PDriveRootPath}' was not found. If this is P:\\, run the tool inside the signed-in office user session or pass the correct path.");
        }

        if (!Directory.Exists(options.CompareRootPath))
        {
            throw new InvalidOperationException($"Compare root '{options.CompareRootPath}' was not found.");
        }

        if (IsSubPath(options.OutputFolder!, options.PDriveRootPath) || IsSubPath(options.OutputFolder!, options.CompareRootPath))
        {
            throw new InvalidOperationException(
                $"Output folder '{options.OutputFolder}' is inside one of the compared folders. Choose a local workstation folder outside both compared roots.");
        }

        var databasePath = Path.Combine(options.OutputFolder!, "projectwise-reconcile.db");
        await using var database = await ProjectWiseReconcileDatabase.CreateAsync(databasePath, cancellationToken);

        var startedUtc = DateTimeOffset.UtcNow;

        var pDriveScanner = new ProjectWiseReconcileScanner(database, logger);
        var pDriveScanStats = await pDriveScanner.ScanAsync(options, cancellationToken);
        logger.Info($"P-drive scan complete. Files={pDriveScanStats.FilesScanned:N0} TotalBytes={pDriveScanStats.TotalBytes:N0}");

        var compareScanner = new ProjectWiseReferenceScanner(database, logger);
        var compareScanStats = await compareScanner.ScanAsync(options, cancellationToken);
        logger.Info($"Compare-root scan complete. Files={compareScanStats.FilesScanned:N0} TotalBytes={compareScanStats.TotalBytes:N0}");

        await database.FinalizeForReportingAsync(cancellationToken);

        var compareRows = await database.GetAllCompareRowsAsync(options.CutoffUtc, options.MatchDateTolerance, cancellationToken);
        var issues = await database.GetImportIssuesAsync(cancellationToken);

        var missingCount = compareRows.LongCount(row => row.MatchStatus == ProjectWiseMatchStatus.MissingFromPw);
        var changedCount = compareRows.LongCount(row => row.ChangedAfterCutoff);
        var cleanupCount = compareRows.LongCount(row => row.MatchStatus == ProjectWiseMatchStatus.MatchedInPw && !row.ChangedAfterCutoff);
        var ambiguousCount = compareRows.LongCount(row => row.MatchStatus == ProjectWiseMatchStatus.AmbiguousNeedsReview);

        var summary = new ProjectWiseReconcileSummary(
            options.PDriveRootPath,
            options.CompareRootPath,
            options.OutputFolder!,
            Environment.MachineName,
            Environment.UserName,
            options.CutoffLocal,
            options.CutoffUtc,
            options.MatchDateToleranceDays,
            startedUtc,
            DateTimeOffset.UtcNow,
            pDriveScanStats.DirectoriesVisited,
            pDriveScanStats.DirectoriesSkipped,
            pDriveScanStats.FilesScanned,
            pDriveScanStats.FilesSkipped,
            pDriveScanStats.InaccessibleCount,
            pDriveScanStats.TotalBytes,
            compareScanStats.DirectoriesVisited,
            compareScanStats.DirectoriesSkipped,
            compareScanStats.FilesScanned,
            compareScanStats.FilesSkipped,
            compareScanStats.InaccessibleCount,
            compareScanStats.TotalBytes,
            issues.Count,
            missingCount,
            changedCount,
            cleanupCount,
            ambiguousCount);

        var reportWriter = new ProjectWiseReconcileReportWriter(logger);
        var artifacts = await reportWriter.WriteAsync(options, summary, compareRows, issues, cancellationToken);

        logger.Info("ProjectWise reconcile finished successfully.");
        return new ProjectWiseReconcileRunResult(summary, artifacts);
    }

    private static void ValidateOptions(ProjectWiseReconcileOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PDriveRootPath))
        {
            throw new InvalidOperationException("P-drive root path is required.");
        }

        if (string.IsNullOrWhiteSpace(options.CompareRootPath))
        {
            throw new InvalidOperationException("Compare root path is required.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputFolder))
        {
            throw new InvalidOperationException("Output folder could not be resolved.");
        }

        if (options.MatchDateToleranceDays < 0)
        {
            throw new InvalidOperationException("MatchDateToleranceDays must be 0 or greater.");
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
