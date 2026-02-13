using System.Text.RegularExpressions;

namespace FileRoutingAgent.Infrastructure.Utilities;

public sealed class GlobMatcher
{
    private readonly Regex[] _fileRegexes;
    private readonly Regex[] _folderRegexes;

    public GlobMatcher(IEnumerable<string> filePatterns, IEnumerable<string> folderPatterns)
    {
        _fileRegexes = filePatterns.Select(ToRegex).ToArray();
        _folderRegexes = folderPatterns.Select(ToRegex).ToArray();
    }

    public bool IsFileIgnored(string path)
    {
        var normalized = NormalizePath(path);
        return _fileRegexes.Any(regex => regex.IsMatch(normalized));
    }

    public bool IsFolderIgnored(string path)
    {
        var normalized = NormalizePath(path);
        return _folderRegexes.Any(regex => regex.IsMatch(normalized));
    }

    private static Regex ToRegex(string globPattern)
    {
        if (string.IsNullOrWhiteSpace(globPattern))
        {
            return new Regex("$a", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        var pattern = Regex.Escape(NormalizePath(globPattern))
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", @"[^\\]*")
            .Replace(@"\?", ".");

        return new Regex($"^{pattern}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('/', '\\');
    }
}

