namespace StorageAudit.Tool;

internal sealed class ProjectWiseReferenceScanner
{
    private readonly ProjectWiseReconcileDatabase _database;
    private readonly AuditLogWriter _logger;

    public ProjectWiseReferenceScanner(ProjectWiseReconcileDatabase database, AuditLogWriter logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task<FolderScanStats> ScanAsync(ProjectWiseReconcileOptions options, CancellationToken cancellationToken)
    {
        var storageOptions = new StorageAuditOptions
        {
            RootPath = options.CompareRootPath,
            IncludeHidden = options.IncludeHidden,
            SkipSystemDirectories = options.SkipSystemDirectories,
            FollowReparsePoints = options.FollowReparsePoints
        };

        var rootDirectory = new DirectoryInfo(options.CompareRootPath);
        var directoriesVisited = 0L;
        var directoriesSkipped = 0L;
        var filesScanned = 0L;
        var filesSkipped = 0L;
        var inaccessibleCount = 0L;
        var totalBytes = 0L;

        var directoryQueue = new Stack<DirectoryInfo>();
        directoryQueue.Push(rootDirectory);

        await using var writeSession = _database.CreateWriteSession();

        while (directoryQueue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentDirectory = directoryQueue.Pop();
            var isRoot = PathsEqual(currentDirectory.FullName, rootDirectory.FullName);

            try
            {
                if (!isRoot && StorageAuditScanner.ShouldSkipDirectory(currentDirectory, storageOptions, out var skipReason))
                {
                    directoriesSkipped++;
                    _logger.Info($"Skipping compare directory: {currentDirectory.FullName} ({skipReason})");
                    continue;
                }
            }
            catch (Exception exception)
            {
                inaccessibleCount++;
                var issue = new ProjectWiseImportIssueRow(null, options.CompareRootPath, exception.GetType().Name, $"Failed to inspect compare directory attributes: {currentDirectory.FullName} - {exception.Message}");
                await writeSession.InsertImportIssueAsync(issue, cancellationToken);
                _logger.Error($"Failed to inspect compare directory attributes: {currentDirectory.FullName}", exception);
                continue;
            }

            directoriesVisited++;

            IEnumerable<DirectoryInfo> subDirectories;
            try
            {
                subDirectories = currentDirectory.EnumerateDirectories(
                    "*",
                    new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = false,
                        ReturnSpecialDirectories = false,
                        AttributesToSkip = 0
                    });
            }
            catch (Exception exception)
            {
                inaccessibleCount++;
                var issue = new ProjectWiseImportIssueRow(null, options.CompareRootPath, exception.GetType().Name, $"Failed to enumerate compare directories under {currentDirectory.FullName}: {exception.Message}");
                await writeSession.InsertImportIssueAsync(issue, cancellationToken);
                _logger.Error($"Failed to enumerate compare directories under {currentDirectory.FullName}", exception);
                continue;
            }

            foreach (var subDirectory in subDirectories)
            {
                directoryQueue.Push(subDirectory);
            }

            IEnumerable<FileInfo> files;
            try
            {
                files = currentDirectory.EnumerateFiles(
                    "*",
                    new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = false,
                        ReturnSpecialDirectories = false,
                        AttributesToSkip = 0
                    });
            }
            catch (Exception exception)
            {
                inaccessibleCount++;
                var issue = new ProjectWiseImportIssueRow(null, options.CompareRootPath, exception.GetType().Name, $"Failed to enumerate compare files under {currentDirectory.FullName}: {exception.Message}");
                await writeSession.InsertImportIssueAsync(issue, cancellationToken);
                _logger.Error($"Failed to enumerate compare files under {currentDirectory.FullName}", exception);
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (StorageAuditScanner.ShouldSkipFile(file, storageOptions, out _))
                    {
                        filesSkipped++;
                        continue;
                    }
                }
                catch (Exception exception)
                {
                    inaccessibleCount++;
                    var issue = new ProjectWiseImportIssueRow(null, options.CompareRootPath, exception.GetType().Name, $"Failed to inspect compare file attributes: {file.FullName} - {exception.Message}");
                    await writeSession.InsertImportIssueAsync(issue, cancellationToken);
                    _logger.Error($"Failed to inspect compare file attributes: {file.FullName}", exception);
                    continue;
                }

                try
                {
                    var row = BuildInventoryRow(file);
                    await writeSession.InsertPwInventoryRowAsync(row, cancellationToken);
                    filesScanned++;
                    totalBytes += row.SizeBytes;

                    if (filesScanned % 10000 == 0)
                    {
                        _logger.Info($"Scanned {filesScanned:N0} compare-root files so far. Latest directory: {currentDirectory.FullName}");
                    }
                }
                catch (Exception exception)
                {
                    inaccessibleCount++;
                    var issue = new ProjectWiseImportIssueRow(null, options.CompareRootPath, exception.GetType().Name, $"Failed to read compare file metadata: {file.FullName} - {exception.Message}");
                    await writeSession.InsertImportIssueAsync(issue, cancellationToken);
                    _logger.Error($"Failed to read compare file metadata: {file.FullName}", exception);
                }
            }
        }

        await writeSession.CompleteAsync(cancellationToken);

        return new FolderScanStats(
            directoriesVisited,
            directoriesSkipped,
            filesScanned,
            filesSkipped,
            inaccessibleCount,
            totalBytes);
    }

    private static ProjectWiseInventoryRow BuildInventoryRow(FileInfo fileInfo)
    {
        var fullPath = fileInfo.FullName.TrimEnd('\\');
        var fileName = fileInfo.Name;
        var fileNameNormalized = fileName.Trim().ToLowerInvariant();
        var parentFolder = fileInfo.DirectoryName ?? string.Empty;
        var lastWriteUtc = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);

        return new ProjectWiseInventoryRow(
            fullPath,
            parentFolder,
            fileName,
            fileNameNormalized,
            fileInfo.Length,
            lastWriteUtc,
            lastWriteUtc.ToUnixTimeSeconds());
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(left.TrimEnd('\\'), right.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }
}
