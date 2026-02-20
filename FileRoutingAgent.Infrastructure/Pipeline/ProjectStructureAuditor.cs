using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class ProjectStructureAuditor(
    IPathCanonicalizer canonicalizer) : IProjectStructureAuditor
{
    public Task<ProjectStructureReport> CheckAsync(ProjectPolicy project, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var projectRoot = DemoModeStateFactory.ResolveProjectRoot(project, canonicalizer);
        var warnings = new List<string>();
        var paths = new List<ProjectStructurePathResult>();
        var allPaths = BuildPathSet(project, projectRoot);

        var duplicates = allPaths
            .GroupBy(path => canonicalizer.Canonicalize(path.Path), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            warnings.Add($"Duplicate configured paths detected: {duplicates.Count}.");
        }

        var canonicalItems = allPaths
            .Select(path => new
            {
                path.Label,
                OriginalPath = path.Path,
                CanonicalPath = canonicalizer.Canonicalize(path.Path)
            })
            .ToList();

        for (var i = 0; i < canonicalItems.Count; i++)
        {
            for (var j = i + 1; j < canonicalItems.Count; j++)
            {
                var left = canonicalItems[i];
                var right = canonicalItems[j];
                if (left.CanonicalPath.Equals(right.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (canonicalizer.PathStartsWith(left.CanonicalPath, right.CanonicalPath) ||
                    canonicalizer.PathStartsWith(right.CanonicalPath, left.CanonicalPath))
                {
                    warnings.Add($"Overlapping path scope: '{left.Label}' and '{right.Label}'.");
                }
            }
        }

        foreach (var item in allPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var canonicalPath = canonicalizer.Canonicalize(item.Path);
            if (string.IsNullOrWhiteSpace(canonicalPath))
            {
                paths.Add(new ProjectStructurePathResult(item.Label, item.Path, StructurePathStatus.Invalid, "Path is empty."));
                continue;
            }

            if (!item.AllowOutsideProjectRoot &&
                !string.IsNullOrWhiteSpace(projectRoot) &&
                !canonicalizer.PathStartsWith(canonicalPath, projectRoot))
            {
                paths.Add(new ProjectStructurePathResult(item.Label, canonicalPath, StructurePathStatus.OutsideProjectRoot, "Path is outside project root."));
                continue;
            }

            if (!Directory.Exists(canonicalPath))
            {
                paths.Add(new ProjectStructurePathResult(item.Label, canonicalPath, StructurePathStatus.Missing, "Directory does not exist."));
                continue;
            }

            try
            {
                _ = Directory.EnumerateFileSystemEntries(canonicalPath).Take(1).ToList();
                paths.Add(new ProjectStructurePathResult(item.Label, canonicalPath, StructurePathStatus.Exists));
            }
            catch (UnauthorizedAccessException exception)
            {
                paths.Add(new ProjectStructurePathResult(item.Label, canonicalPath, StructurePathStatus.AccessDenied, exception.Message));
            }
            catch (Exception exception)
            {
                paths.Add(new ProjectStructurePathResult(item.Label, canonicalPath, StructurePathStatus.Invalid, exception.Message));
            }
        }

        var report = new ProjectStructureReport(
            project.ProjectId,
            projectRoot,
            paths,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        return Task.FromResult(report);
    }

    private static List<PathLabel> BuildPathSet(ProjectPolicy project, string projectRoot)
    {
        var result = new List<PathLabel>();
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            result.Add(new PathLabel("Project Root", projectRoot, AllowOutsideProjectRoot: true));
        }

        foreach (var workingRoot in project.WorkingRoots)
        {
            result.Add(new PathLabel("Working Root", workingRoot, AllowOutsideProjectRoot: false));
        }

        result.Add(new PathLabel("Official CAD Publish", project.OfficialDestinations.CadPublish, AllowOutsideProjectRoot: false));
        result.Add(new PathLabel("Official Plot Sets", project.OfficialDestinations.PlotSets, AllowOutsideProjectRoot: false));

        foreach (var (category, destination) in project.OfficialDestinations.PdfCategories)
        {
            result.Add(new PathLabel($"PDF Category '{category}'", destination, AllowOutsideProjectRoot: false));
        }

        return result
            .Where(path => !string.IsNullOrWhiteSpace(path.Path))
            .ToList();
    }

    private sealed record PathLabel(string Label, string Path, bool AllowOutsideProjectRoot);
}
