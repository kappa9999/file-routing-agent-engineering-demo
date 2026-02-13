using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class RoutingRulesEngine : IRoutingRulesEngine
{
    public RouteResult ResolveRoute(ClassifiedFile file, ProjectPolicy project, UserDecision decision, IPathCanonicalizer canonicalizer)
    {
        var sourceName = Path.GetFileName(file.File.SourcePath);
        var destinationRoot = ResolveDestinationRoot(file, project, decision);
        var destinationPath = canonicalizer.Canonicalize(Path.Combine(destinationRoot, sourceName));

        return new RouteResult(
            destinationPath,
            IsOfficialDestination: true,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["projectId"] = project.ProjectId,
                ["category"] = file.Category.ToString(),
                ["action"] = decision.Action.ToString()
            });
    }

    private static string ResolveDestinationRoot(ClassifiedFile file, ProjectPolicy project, UserDecision decision)
    {
        return file.Category switch
        {
            FileCategory.Pdf => ResolvePdfDestination(project, decision.PdfCategoryKey),
            FileCategory.PlotSet => project.OfficialDestinations.PlotSets,
            FileCategory.Cad => project.OfficialDestinations.CadPublish,
            _ => project.OfficialDestinations.CadPublish
        };
    }

    private static string ResolvePdfDestination(ProjectPolicy project, string? categoryKey)
    {
        if (!string.IsNullOrWhiteSpace(categoryKey) &&
            project.OfficialDestinations.PdfCategories.TryGetValue(categoryKey, out var configuredPath))
        {
            return configuredPath;
        }

        if (project.OfficialDestinations.PdfCategories.TryGetValue(project.Defaults.DefaultPdfCategory, out configuredPath))
        {
            return configuredPath;
        }

        return project.OfficialDestinations.PdfCategories.Values.FirstOrDefault()
               ?? project.OfficialDestinations.CadPublish;
    }
}

