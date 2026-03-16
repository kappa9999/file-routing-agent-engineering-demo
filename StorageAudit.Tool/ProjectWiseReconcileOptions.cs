using System.Globalization;

namespace StorageAudit.Tool;

public sealed record ProjectWiseReconcileOptions
{
    private static readonly DateTime DefaultCutoffDate = new(2025, 4, 1, 0, 0, 0, DateTimeKind.Unspecified);

    public string PDriveRootPath { get; init; } = @"P:\1000_Software";
    public string CompareRootPath { get; init; } = string.Empty;
    public string? OutputFolder { get; init; }
    public DateTimeOffset CutoffLocal { get; init; } = CreateDefaultCutoff();
    public int MatchDateToleranceDays { get; init; } = 2;
    public bool IncludeHidden { get; init; } = true;
    public bool SkipSystemDirectories { get; init; } = true;
    public bool FollowReparsePoints { get; init; }

    public DateTimeOffset CutoffUtc => CutoffLocal.ToUniversalTime();
    public TimeSpan MatchDateTolerance => TimeSpan.FromDays(MatchDateToleranceDays);

    public ProjectWiseReconcileOptions Normalize(DateTimeOffset now)
    {
        var normalizedPDriveRoot = NormalizePath(PDriveRootPath);
        var normalizedCompareRoot = NormalizePath(CompareRootPath);
        var outputFolder = string.IsNullOrWhiteSpace(OutputFolder)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "FileStorageAudit",
                $"PwReconcile_{now:yyyyMMdd_HHmmss}")
            : NormalizePath(OutputFolder);

        return this with
        {
            PDriveRootPath = normalizedPDriveRoot,
            CompareRootPath = normalizedCompareRoot,
            OutputFolder = outputFolder
        };
    }

    public static bool TryParse(string[] args, out ProjectWiseReconcileOptions? options, out string? error, out bool showHelp)
    {
        showHelp = false;
        error = null;

        var parsed = new ProjectWiseReconcileOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg.ToLowerInvariant())
            {
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    options = null;
                    return true;
                case "--p-root":
                    if (!TryReadValue(args, ref index, out var pRoot, out error))
                    {
                        options = null;
                        return false;
                    }

                    parsed = parsed with { PDriveRootPath = pRoot };
                    break;
                case "--compare-root":
                    if (!TryReadValue(args, ref index, out var compareRoot, out error))
                    {
                        options = null;
                        return false;
                    }

                    parsed = parsed with { CompareRootPath = compareRoot };
                    break;
                case "--output-folder":
                    if (!TryReadValue(args, ref index, out var outputFolder, out error))
                    {
                        options = null;
                        return false;
                    }

                    parsed = parsed with { OutputFolder = outputFolder };
                    break;
                case "--cutoff-date":
                    if (!TryReadValue(args, ref index, out var cutoffRaw, out error))
                    {
                        options = null;
                        return false;
                    }

                    if (!TryParseCutoffDate(cutoffRaw, out var cutoffLocal, out error))
                    {
                        options = null;
                        return false;
                    }

                    parsed = parsed with { CutoffLocal = cutoffLocal };
                    break;
                default:
                    error = $"Unknown argument '{arg}'.";
                    options = null;
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(parsed.PDriveRootPath))
        {
            error = "--p-root is required.";
            options = null;
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.CompareRootPath))
        {
            error = "--compare-root is required.";
            options = null;
            return false;
        }

        options = parsed;
        return true;
    }

    public static string BuildUsage()
    {
        return """
               ProjectWise Reconcile Mode

               Usage:
                 StorageAudit.Tool.exe reconcile-pw --p-root <path> --compare-root <folder> [options]

               Options:
                 --p-root <path>         P-drive source folder. Default: P:\1000_Software
                 --compare-root <path>   Local copy of the ProjectWise folder to compare against.
                 --cutoff-date <date>    Migration cutoff date. Default: 2025-04-01
                 --output-folder <path>  Local output folder. Default: %USERPROFILE%\Documents\FileStorageAudit\PwReconcile_<timestamp>
                 --help                  Show this message

               Notes:
                 - This mode is read-only against both folders.
                 - It compares P: directly against a local ProjectWise folder copy.
                 - It blocks output folders placed under either compared folder.
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

    private static bool TryParseCutoffDate(string raw, out DateTimeOffset cutoffLocal, out string? error)
    {
        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            cutoffLocal = CreateLocalCutoff(dateOnly.ToDateTime(TimeOnly.MinValue));
            error = null;
            return true;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var parsedOffset))
        {
            cutoffLocal = parsedOffset.ToLocalTime();
            error = null;
            return true;
        }

        error = $"Invalid cutoff date '{raw}'. Use yyyy-MM-dd or an ISO 8601 date/time.";
        cutoffLocal = default;
        return false;
    }

    private static DateTimeOffset CreateDefaultCutoff()
    {
        return CreateLocalCutoff(DefaultCutoffDate);
    }

    private static DateTimeOffset CreateLocalCutoff(DateTime localDateTime)
    {
        var local = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
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
