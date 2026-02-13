using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class ProjectRegistry(IPathCanonicalizer canonicalizer) : IProjectRegistry
{
    public ProjectResolution? Resolve(ClassifiedFile file, FirmPolicy policy)
    {
        ProjectPolicy? bestMatch = null;
        string? bestPrefix = null;
        var sourcePath = canonicalizer.Canonicalize(file.File.SourcePath);

        foreach (var project in policy.Projects)
        {
            foreach (var matcher in project.PathMatchers)
            {
                var candidatePrefix = canonicalizer.Canonicalize(matcher);
                if (!canonicalizer.PathStartsWith(sourcePath, candidatePrefix))
                {
                    continue;
                }

                if (bestPrefix is null || candidatePrefix.Length > bestPrefix.Length)
                {
                    bestPrefix = candidatePrefix;
                    bestMatch = project;
                }
            }
        }

        if (bestMatch is null)
        {
            return null;
        }

        return new ProjectResolution(bestMatch.ProjectId, bestMatch.DisplayName, bestPrefix);
    }

    public bool IsInCandidateRoot(string path, FirmPolicy policy)
    {
        return policy.Monitoring.CandidateRoots.Any(root => canonicalizer.PathStartsWith(path, root));
    }

    public bool IsInOfficialDestination(string path, ProjectPolicy project, IPathCanonicalizer pathCanonicalizer)
    {
        if (pathCanonicalizer.PathStartsWith(path, project.OfficialDestinations.CadPublish))
        {
            return true;
        }

        if (pathCanonicalizer.PathStartsWith(path, project.OfficialDestinations.PlotSets))
        {
            return true;
        }

        return project.OfficialDestinations.PdfCategories.Values.Any(destination => pathCanonicalizer.PathStartsWith(path, destination));
    }
}

