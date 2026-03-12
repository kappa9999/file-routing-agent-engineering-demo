using System.Globalization;

namespace StorageAudit.Tool;

public sealed record StorageAuditOptions
{
    public string RootPath { get; init; } = @"P:\";
    public string? OutputFolder { get; init; }
    public int TopFilesCount { get; init; } = 2000;
    public int CandidateMinSizeMb { get; init; } = 250;
    public int CandidateMinAgeDays { get; init; } = 365;
    public int ArchiveProjectAgeDays { get; init; } = 730;
    public bool FollowReparsePoints { get; init; }
    public bool IncludeHidden { get; init; } = true;
    public bool SkipSystemDirectories { get; init; } = true;

    public long CandidateMinSizeBytes => CandidateMinSizeMb * 1024L * 1024L;

    public StorageAuditOptions Normalize(DateTimeOffset now)
    {
        var normalizedRoot = NormalizePath(RootPath);
        var outputFolder = string.IsNullOrWhiteSpace(OutputFolder)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "FileStorageAudit",
                $"Audit_{now:yyyyMMdd_HHmmss}")
            : NormalizePath(OutputFolder);

        return this with
        {
            RootPath = normalizedRoot,
            OutputFolder = outputFolder
        };
    }

    public static bool TryParse(string[] args, out StorageAuditOptions? options, out string? error, out bool showHelp)
    {
        showHelp = false;
        error = null;

        var parsed = new StorageAuditOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    options = null;
                    return true;
                case "--root":
                    if (!TryReadValue(args, ref i, out var rootPath, out error))
                    {
                        options = null;
                        return false;
                    }

                    parsed = parsed with { RootPath = rootPath };
                    break;
                case "--output-folder":
                    if (!TryReadValue(args, ref i, out var outputFolder, out error))
                    {
                        options = null;
                        return false;
                    }

                    parsed = parsed with { OutputFolder = outputFolder };
                    break;
                case "--top-files-count":
                    if (!TryReadInt(args, ref i, out var topFilesCount, out error) || topFilesCount <= 0)
                    {
                        error ??= "--top-files-count must be greater than 0.";
                        options = null;
                        return false;
                    }

                    parsed = parsed with { TopFilesCount = topFilesCount };
                    break;
                case "--candidate-min-size-mb":
                    if (!TryReadInt(args, ref i, out var minSizeMb, out error) || minSizeMb < 0)
                    {
                        error ??= "--candidate-min-size-mb must be 0 or greater.";
                        options = null;
                        return false;
                    }

                    parsed = parsed with { CandidateMinSizeMb = minSizeMb };
                    break;
                case "--candidate-min-age-days":
                    if (!TryReadInt(args, ref i, out var minAgeDays, out error) || minAgeDays < 0)
                    {
                        error ??= "--candidate-min-age-days must be 0 or greater.";
                        options = null;
                        return false;
                    }

                    parsed = parsed with { CandidateMinAgeDays = minAgeDays };
                    break;
                case "--archive-project-age-days":
                    if (!TryReadInt(args, ref i, out var archiveAgeDays, out error) || archiveAgeDays < 0)
                    {
                        error ??= "--archive-project-age-days must be 0 or greater.";
                        options = null;
                        return false;
                    }

                    parsed = parsed with { ArchiveProjectAgeDays = archiveAgeDays };
                    break;
                case "--include-hidden":
                    if (!TryReadBool(args, ref i, out var includeHidden, out error))
                    {
                        options = null;
                        return false;
                    }

                    parsed = parsed with { IncludeHidden = includeHidden };
                    break;
                case "--skip-system-directories":
                    if (!TryReadBool(args, ref i, out var skipSystemDirectories, out error))
                    {
                        options = null;
                        return false;
                    }

                    parsed = parsed with { SkipSystemDirectories = skipSystemDirectories };
                    break;
                default:
                    error = $"Unknown argument '{arg}'.";
                    options = null;
                    return false;
            }
        }

        options = parsed;
        return true;
    }

    public static string BuildUsage()
    {
        return """
               Storage Audit Tool

               Usage:
                 StorageAudit.Tool.exe [options]

               Options:
                 --root <path>                      Scan root. Default: P:\
                 --output-folder <path>             Local output folder. Default: %USERPROFILE%\Documents\FileStorageAudit\Audit_<timestamp>
                 --top-files-count <number>         Largest Files row count. Default: 2000
                 --candidate-min-size-mb <number>   Candidate Review minimum size in MB. Default: 250
                 --candidate-min-age-days <number>  Candidate Review minimum age in days. Default: 365
                 --archive-project-age-days <num>   Project archive-review threshold. Default: 730
                 --include-hidden <true|false>      Include hidden files/folders. Default: true
                 --skip-system-directories <bool>   Skip system directories. Default: true
                 --help                             Show this message

               Notes:
                 - This tool is read-only against the scan root.
                 - If P:\ is not visible, run it inside the signed-in office user session or pass a UNC path with --root.
               """;
    }

    private static bool TryReadValue(string[] args, ref int index, out string value, out string? error)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            error = $"Missing value for '{args[index]}'.";
            return false;
        }

        index++;
        value = args[index];
        error = null;
        return true;
    }

    private static bool TryReadInt(string[] args, ref int index, out int value, out string? error)
    {
        value = 0;
        if (!TryReadValue(args, ref index, out var raw, out error))
        {
            return false;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = $"Invalid integer value '{raw}' for '{args[index - 1]}'.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryReadBool(string[] args, ref int index, out bool value, out string? error)
    {
        value = false;
        if (!TryReadValue(args, ref index, out var raw, out error))
        {
            return false;
        }

        if (!bool.TryParse(raw, out value))
        {
            error = $"Invalid boolean value '{raw}' for '{args[index - 1]}'. Use true or false.";
            return false;
        }

        error = null;
        return true;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (expanded.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return expanded.TrimEnd('\\');
        }

        return Path.GetFullPath(expanded).TrimEnd('\\');
    }
}
