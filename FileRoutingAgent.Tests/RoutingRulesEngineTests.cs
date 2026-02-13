using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Infrastructure.Pathing;
using FileRoutingAgent.Infrastructure.Pipeline;

namespace FileRoutingAgent.Tests;

public sealed class RoutingRulesEngineTests
{
    [Fact]
    public void ResolveRoute_ForPdf_UsesSelectedCategoryDestination()
    {
        var engine = new RoutingRulesEngine();
        var canonicalizer = new PathCanonicalizer();
        var project = new ProjectPolicy
        {
            ProjectId = "P1",
            DisplayName = "P1",
            OfficialDestinations = new OfficialDestinations
            {
                CadPublish = @"C:\dest\cad",
                PlotSets = @"C:\dest\plot",
                PdfCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["progress_print"] = @"C:\dest\progress",
                    ["exhibit"] = @"C:\dest\exhibit"
                }
            },
            Defaults = new ProjectDefaults
            {
                DefaultPdfCategory = "progress_print"
            }
        };

        var stable = new StableFile(@"C:\src\sheet01.pdf", 100, DateTime.UtcNow, "f");
        var classified = new ClassifiedFile(stable, FileCategory.Pdf, ".pdf");
        var decision = new UserDecision(ProposedAction.Move, "exhibit");

        var route = engine.ResolveRoute(classified, project, decision, canonicalizer);
        Assert.Equal(@"C:\dest\exhibit\sheet01.pdf", route.DestinationPath);
    }
}

