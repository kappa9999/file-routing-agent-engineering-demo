using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class DemoMirrorService(
    IPathCanonicalizer canonicalizer) : IDemoMirrorService
{
    private static readonly string[] DefaultIgnoredFolderPrefixes =
    [
        "dms",
        "_PW_"
    ];

    public Task<DemoMirrorRefreshResult> RefreshAsync(ProjectPolicy project, DemoModeState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var liveRoot = DemoModeStateFactory.ResolveProjectRoot(project, canonicalizer);
        if (string.IsNullOrWhiteSpace(liveRoot))
        {
            return Task.FromResult(new DemoMirrorRefreshResult(
                project.ProjectId,
                string.Empty,
                string.Empty,
                0,
                0,
                0,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["project_root"] = "Project root could not be resolved from path matchers."
                }));
        }

        var mirrorRoot = state.ProjectMirrorRoots.TryGetValue(project.ProjectId, out var configuredMirrorRoot) &&
                         !string.IsNullOrWhiteSpace(configuredMirrorRoot)
            ? canonicalizer.Canonicalize(configuredMirrorRoot)
            : canonicalizer.Canonicalize(Path.Combine(liveRoot, state.MirrorFolderName));

        var createdCount = 0;
        var existingCount = 0;
        var skippedCount = 0;
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(liveRoot))
        {
            errors[liveRoot] = "Live project root not found.";
            return Task.FromResult(new DemoMirrorRefreshResult(
                project.ProjectId,
                liveRoot,
                mirrorRoot,
                createdCount,
                existingCount,
                skippedCount,
                errors));
        }

        EnsureDirectory(mirrorRoot, ref createdCount, ref existingCount, errors);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.System
        };

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(liveRoot, "*", options);
        }
        catch (Exception exception)
        {
            errors[liveRoot] = exception.Message;
            return Task.FromResult(new DemoMirrorRefreshResult(
                project.ProjectId,
                liveRoot,
                mirrorRoot,
                createdCount,
                existingCount,
                skippedCount,
                errors));
        }

        foreach (var sourceDirectory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var canonicalSourceDirectory = canonicalizer.Canonicalize(sourceDirectory);
            if (canonicalizer.PathStartsWith(canonicalSourceDirectory, mirrorRoot))
            {
                skippedCount++;
                continue;
            }

            var directoryName = Path.GetFileName(canonicalSourceDirectory) ?? string.Empty;
            if (directoryName.Equals(state.MirrorFolderName, StringComparison.OrdinalIgnoreCase) ||
                directoryName.Equals(".pwcache", StringComparison.OrdinalIgnoreCase) ||
                DefaultIgnoredFolderPrefixes.Any(prefix => directoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                skippedCount++;
                continue;
            }

            try
            {
                var attributes = File.GetAttributes(canonicalSourceDirectory);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skippedCount++;
                    continue;
                }
            }
            catch (Exception exception)
            {
                errors[canonicalSourceDirectory] = exception.Message;
                continue;
            }

            var relativePath = Path.GetRelativePath(liveRoot, canonicalSourceDirectory);
            if (relativePath.StartsWith("..", StringComparison.OrdinalIgnoreCase))
            {
                skippedCount++;
                continue;
            }

            var mirrorDirectory = canonicalizer.Canonicalize(Path.Combine(mirrorRoot, relativePath));
            EnsureDirectory(mirrorDirectory, ref createdCount, ref existingCount, errors);
        }

        return Task.FromResult(new DemoMirrorRefreshResult(
            project.ProjectId,
            liveRoot,
            mirrorRoot,
            createdCount,
            existingCount,
            skippedCount,
            errors));
    }

    private static void EnsureDirectory(
        string path,
        ref int createdCount,
        ref int existingCount,
        IDictionary<string, string> errors)
    {
        try
        {
            if (Directory.Exists(path))
            {
                existingCount++;
                return;
            }

            Directory.CreateDirectory(path);
            createdCount++;
        }
        catch (Exception exception)
        {
            errors[path] = exception.Message;
        }
    }
}
