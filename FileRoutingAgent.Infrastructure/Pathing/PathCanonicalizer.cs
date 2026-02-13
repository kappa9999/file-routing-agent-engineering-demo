using System.Runtime.InteropServices;
using FileRoutingAgent.Core.Interfaces;

namespace FileRoutingAgent.Infrastructure.Pathing;

public sealed class PathCanonicalizer : IPathCanonicalizer
{
    public string Canonicalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());

        try
        {
            expanded = Path.GetFullPath(expanded);
        }
        catch
        {
            // Keep the original expanded path if full path resolution fails.
        }

        expanded = expanded.Replace('/', '\\').TrimEnd('\\');

        var unc = ResolveMappedDriveToUnc(expanded);
        return unc.Replace('/', '\\').TrimEnd('\\');
    }

    public bool PathStartsWith(string path, string root)
    {
        var canonicalPath = Canonicalize(path);
        var canonicalRoot = Canonicalize(root);
        if (string.IsNullOrWhiteSpace(canonicalRoot))
        {
            return false;
        }

        var rootWithSlash = $"{canonicalRoot}\\";
        return canonicalPath.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase)
               || canonicalPath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveMappedDriveToUnc(string path)
    {
        if (path.Length < 2 || path[1] != ':')
        {
            return path;
        }

        var drive = path[..2];
        var builder = new System.Text.StringBuilder(512);
        var size = builder.Capacity;
        var result = WNetGetConnection(drive, builder, ref size);
        if (result != 0 || builder.Length == 0)
        {
            return path;
        }

        var remainder = path.Length > 2 ? path[2..] : string.Empty;
        return $"{builder}{remainder}";
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnection(
        string localName,
        System.Text.StringBuilder remoteName,
        ref int length);
}

