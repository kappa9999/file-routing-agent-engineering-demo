namespace StorageAudit.Tool;

public sealed class StorageAuditScanner
{
    private readonly StorageAuditDatabase _database;
    private readonly AuditLogWriter _logger;

    public StorageAuditScanner(StorageAuditDatabase database, AuditLogWriter logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task<ScanRunSummary> ScanAsync(StorageAuditOptions options, CancellationToken cancellationToken)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var rootDirectory = new DirectoryInfo(options.RootPath);
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

            string skipReason;
            try
            {
                if (!isRoot && ShouldSkipDirectory(currentDirectory, options, out skipReason))
                {
                    directoriesSkipped++;
                    _logger.Info($"Skipping directory: {currentDirectory.FullName} ({skipReason})");
                    continue;
                }
            }
            catch (Exception exception)
            {
                inaccessibleCount++;
                await writeSession.InsertIssueAsync(
                    new ScanIssueRow(currentDirectory.FullName, exception.GetType().Name, exception.Message),
                    cancellationToken);
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
                await writeSession.InsertIssueAsync(
                    new ScanIssueRow(currentDirectory.FullName, exception.GetType().Name, exception.Message),
                    cancellationToken);
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
                await writeSession.InsertIssueAsync(
                    new ScanIssueRow(currentDirectory.FullName, exception.GetType().Name, exception.Message),
                    cancellationToken);
                _logger.Error($"Failed to enumerate files under {currentDirectory.FullName}", exception);
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (ShouldSkipFile(file, options, out _))
                    {
                        filesSkipped++;
                        continue;
                    }
                }
                catch (Exception exception)
                {
                    inaccessibleCount++;
                    await writeSession.InsertIssueAsync(
                        new ScanIssueRow(file.FullName, exception.GetType().Name, exception.Message),
                        cancellationToken);
                    _logger.Error($"Failed to inspect file attributes: {file.FullName}", exception);
                    continue;
                }

                try
                {
                    var row = BuildFileInventoryRow(file, rootDirectory.FullName, startedUtc);
                    await writeSession.InsertFileAsync(row, cancellationToken);
                    filesScanned++;
                    totalBytes += row.SizeBytes;

                    if (filesScanned % 10000 == 0)
                    {
                        _logger.Info($"Scanned {filesScanned:N0} files so far. Latest directory: {currentDirectory.FullName}");
                    }
                }
                catch (Exception exception)
                {
                    inaccessibleCount++;
                    await writeSession.InsertIssueAsync(
                        new ScanIssueRow(file.FullName, exception.GetType().Name, exception.Message),
                        cancellationToken);
                    _logger.Error($"Failed to read file metadata: {file.FullName}", exception);
                }
            }
        }

        await writeSession.CompleteAsync(cancellationToken);

        return new ScanRunSummary(
            options.RootPath,
            options.OutputFolder ?? string.Empty,
            Environment.MachineName,
            Environment.UserName,
            startedUtc,
            DateTimeOffset.UtcNow,
            directoriesVisited,
            directoriesSkipped,
            filesScanned,
            filesSkipped,
            inaccessibleCount,
            totalBytes);
    }

    public static bool ShouldSkipDirectory(DirectoryInfo directoryInfo, StorageAuditOptions options, out string reason)
    {
        var attributes = directoryInfo.Attributes;
        if (!options.FollowReparsePoints && attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            reason = "reparse-point";
            return true;
        }

        if (!options.IncludeHidden && attributes.HasFlag(FileAttributes.Hidden))
        {
            reason = "hidden";
            return true;
        }

        if (options.SkipSystemDirectories && attributes.HasFlag(FileAttributes.System))
        {
            reason = "system";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    public static bool ShouldSkipFile(FileInfo fileInfo, StorageAuditOptions options, out string reason)
    {
        var attributes = fileInfo.Attributes;
        if (!options.IncludeHidden && attributes.HasFlag(FileAttributes.Hidden))
        {
            reason = "hidden";
            return true;
        }

        if (!options.FollowReparsePoints && attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            reason = "reparse-point";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static FileInventoryRow BuildFileInventoryRow(FileInfo fileInfo, string rootPath, DateTimeOffset nowUtc)
    {
        var fullPath = fileInfo.FullName.TrimEnd('\\');
        var parentFolder = fileInfo.DirectoryName ?? string.Empty;
        var extension = fileInfo.Extension.ToLowerInvariant();
        var createdUtc = new DateTimeOffset(fileInfo.CreationTimeUtc, TimeSpan.Zero);
        var lastWriteUtc = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);
        var ageDays = Math.Max(0, (int)Math.Floor((nowUtc - lastWriteUtc).TotalDays));
        var projectBucket = ResolveProjectBucket(rootPath, fullPath);

        return new FileInventoryRow(
            fullPath,
            parentFolder,
            projectBucket,
            extension,
            fileInfo.Length,
            createdUtc,
            lastWriteUtc,
            ageDays);
    }

    private static string ResolveProjectBucket(string rootPath, string filePath)
    {
        var normalizedRoot = rootPath.TrimEnd('\\');
        if (!filePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return "(outside-root)";
        }

        var relative = filePath.Length == normalizedRoot.Length
            ? string.Empty
            : filePath[normalizedRoot.Length..].TrimStart('\\');
        if (string.IsNullOrWhiteSpace(relative))
        {
            return "(root)";
        }

        var separatorIndex = relative.IndexOf('\\');
        return separatorIndex < 0 ? "(root)" : relative[..separatorIndex];
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            left.TrimEnd('\\'),
            right.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
    }
}
