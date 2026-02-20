using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Infrastructure.Pathing;
using FileRoutingAgent.Infrastructure.Pipeline;

namespace FileRoutingAgent.Tests;

public sealed class DemoModeServicesTests
{
    [Fact]
    public void DemoSnapshotTransformer_MapsProjectRootsToMirror()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "FraDemoSnapshotTests", Guid.NewGuid().ToString("N"));
        var mirrorRoot = Path.Combine(projectRoot, "_FRA_Demo");
        var policy = BuildPolicy(projectRoot);
        var preferences = new UserPreferences
        {
            DemoModeEnabled = true,
            DemoMirrorFolderName = "_FRA_Demo",
            DemoMirrorRootsByProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Project123"] = mirrorRoot
            }
        };

        var snapshot = new RuntimeConfigSnapshot
        {
            PolicyPath = "policy.json",
            UserPreferencesPath = "prefs.json",
            Policy = policy,
            UserPreferences = preferences,
            SafeModeEnabled = false
        };

        var canonicalizer = new PathCanonicalizer();
        var state = DemoModeStateFactory.Resolve(snapshot, canonicalizer);
        var transformer = new DemoSnapshotTransformer();
        var transformed = transformer.ApplyDemoOverlay(snapshot, state, canonicalizer);

        Assert.True(transformed.DemoMode.Enabled);
        Assert.All(transformed.Policy.Projects, project =>
        {
            Assert.All(project.PathMatchers, matcher => Assert.StartsWith(canonicalizer.Canonicalize(mirrorRoot), canonicalizer.Canonicalize(matcher), StringComparison.OrdinalIgnoreCase));
            Assert.All(project.WorkingRoots, root => Assert.StartsWith(canonicalizer.Canonicalize(mirrorRoot), canonicalizer.Canonicalize(root), StringComparison.OrdinalIgnoreCase));
            Assert.StartsWith(canonicalizer.Canonicalize(mirrorRoot), canonicalizer.Canonicalize(project.OfficialDestinations.CadPublish), StringComparison.OrdinalIgnoreCase);
        });
        Assert.All(transformed.Policy.Monitoring.WatchRoots, root => Assert.StartsWith(canonicalizer.Canonicalize(mirrorRoot), canonicalizer.Canonicalize(root), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DemoSafetyGuard_BlocksDestinationOutsideMirror()
    {
        var mirrorRoot = Path.Combine(Path.GetTempPath(), "FraDemoSafetyTests", Guid.NewGuid().ToString("N"), "_FRA_Demo");
        var source = Path.Combine(mirrorRoot, "70_Design", "_Working", "test.pdf");
        var destination = Path.Combine(Path.GetPathRoot(mirrorRoot) ?? "C:\\", "live", "test.pdf");

        var plan = new TransferPlan(
            new ClassifiedFile(new StableFile(source, 10, DateTime.UtcNow, "fp1"), FileCategory.Pdf, ".pdf"),
            new ProjectResolution("Project123", "Project 123", mirrorRoot),
            new UserDecision(ProposedAction.Move, "progress_print"),
            new RouteResult(destination, false),
            new ConflictPlan(destination, HasConflict: false, ExistingPath: null, Choice: ConflictChoice.KeepBothVersioned, ValidationErrors: []));

        var state = new DemoModeState(
            Enabled: true,
            MirrorFolderName: "_FRA_Demo",
            ProjectMirrorRoots: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Project123"] = mirrorRoot
            },
            LastRefreshedUtc: DateTime.UtcNow);

        var guard = new DemoSafetyGuard(new PathCanonicalizer());
        var allowed = guard.IsAllowed(plan, state, out var reason);

        Assert.False(allowed);
        Assert.Contains("Destination path is outside demo mirror scope", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DemoMirrorService_CreatesFolderOnlyMirrorWithoutCopyingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "FraDemoMirrorTests", Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "Project123");
        var liveNested = Path.Combine(projectRoot, "60_CAD", "70_Working", "AK");
        Directory.CreateDirectory(liveNested);
        await File.WriteAllTextAsync(Path.Combine(liveNested, "livefile.pdf"), "demo");

        var policy = BuildPolicy(projectRoot);
        var mirrorRoot = Path.Combine(projectRoot, "_FRA_Demo");
        var state = new DemoModeState(
            Enabled: true,
            MirrorFolderName: "_FRA_Demo",
            ProjectMirrorRoots: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Project123"] = mirrorRoot
            },
            LastRefreshedUtc: null);

        var service = new DemoMirrorService(new PathCanonicalizer());
        var result = await service.RefreshAsync(policy.Projects[0], state, CancellationToken.None);

        Assert.True(Directory.Exists(mirrorRoot));
        Assert.True(Directory.Exists(Path.Combine(mirrorRoot, "60_CAD", "70_Working", "AK")));
        Assert.False(File.Exists(Path.Combine(mirrorRoot, "60_CAD", "70_Working", "AK", "livefile.pdf")));
        Assert.True(result.CreatedCount > 0 || result.ExistingCount > 0);
    }

    private static FirmPolicy BuildPolicy(string projectRoot)
    {
        return new FirmPolicy
        {
            Monitoring = new MonitoringSettings
            {
                CandidateRoots = [Path.Combine(projectRoot, "70_Design", "_Working"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")],
                WatchRoots = [projectRoot]
            },
            ManagedExtensions = [".pdf"],
            Projects =
            [
                new ProjectPolicy
                {
                    ProjectId = "Project123",
                    DisplayName = "Project 123",
                    PathMatchers = [projectRoot],
                    WorkingRoots = [Path.Combine(projectRoot, "60_CAD", "70_Working"), Path.Combine(projectRoot, "70_Design", "_Working")],
                    OfficialDestinations = new OfficialDestinations
                    {
                        CadPublish = Path.Combine(projectRoot, "60_CAD", "Published"),
                        PlotSets = Path.Combine(projectRoot, "70_Design", "90_PlotSets"),
                        PdfCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["progress_print"] = Path.Combine(projectRoot, "70_Design", "10_ProgressPrints")
                        }
                    },
                    Defaults = new ProjectDefaults
                    {
                        DefaultPdfCategory = "progress_print"
                    }
                }
            ]
        };
    }
}
