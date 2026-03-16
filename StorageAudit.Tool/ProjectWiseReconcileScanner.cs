namespace StorageAudit.Tool;

internal sealed class ProjectWiseReconcileScanner
{
    private readonly ProjectWiseReconcileDatabase _database;
    private readonly AuditLogWriter _logger;

    public ProjectWiseReconcileScanner(ProjectWiseReconcileDatabase database, AuditLogWriter logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task<FolderScanStats> ScanAsync(ProjectWiseReconcileOptions options, CancellationToken cancellationToken)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var storageOptions = new StorageAuditOptions
        {
            RootPath = options.PDriveRootPath,
            IncludeHidden = options.IncludeHidden,
            SkipSystemDirectories = options.SkipSystemDirectories,
            FollowReparsePoints = options.FollowReparsePoints
        };

        var rootDirectory = new DirectoryInfo(options.PDriveRootPath);
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
                    _logger.Info($"Skipping directory: {currentDirectory.FullName} ({skipReason})");
                    continue;
                }
            }
            catch (Exception exception)
            {
                inaccessibleCount++;
                _logger.Error($"Failed to inspect directory attributes: {currentDirectory.FullName}", exception);
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
                _logger.Error($"Failed to enumerate directories under {currentDirectory.FullName}", exception);
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
                _logger.Error($"Failed to enumerate files under {currentDirectory.FullName}", exception);
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
                    _logger.Error($"Failed to inspect file attributes: {file.FullName}", exception);
                    continue;
                }

                try
                {
                    var row = BuildInventoryRow(file, startedUtc);
                    await writeSession.InsertPDriveFileAsync(row, cancellationToken);
                    filesScanned++;
                    totalBytes += row.SizeBytes;

                    if (filesScanned % 10000 == 0)
                    {
                        _logger.Info($"Scanned {filesScanned:N0} P-drive files so far. Latest directory: {currentDirectory.FullName}");
                    }
                }
                catch (Exception exception)
                {
                    inaccessibleCount++;
                    _logger.Error($"Failed to read P-drive file metadata: {file.FullName}", exception);
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

    private static PDriveInventoryRow BuildInventoryRow(FileInfo fileInfo, DateTimeOffset nowUtc)
    {
        var fullPath = fileInfo.FullName.TrimEnd('\\');
        var fileName = fileInfo.Name;
        var fileNameNormalized = fileName.Trim().ToLowerInvariant();
        var parentFolder = fileInfo.DirectoryName ?? string.Empty;
        var extension = fileInfo.Extension.ToLowerInvariant();
        var createdUtc = new DateTimeOffset(fileInfo.CreationTimeUtc, TimeSpan.Zero);
        var lastWriteUtc = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);
        var ageDays = Math.Max(0, (int)Math.Floor((nowUtc - lastWriteUtc).TotalDays));

        return new PDriveInventoryRow(
            fullPath,
            parentFolder,
            fileName,
            fileNameNormalized,
            extension,
            fileInfo.Length,
            createdUtc,
            lastWriteUtc,
            lastWriteUtc.ToUnixTimeSeconds(),
            ageDays);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(left.TrimEnd('\\'), right.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }
}
