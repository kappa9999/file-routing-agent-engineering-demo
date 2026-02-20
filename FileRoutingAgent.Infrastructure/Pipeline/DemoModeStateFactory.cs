using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public static class DemoModeStateFactory
{
    private const string DefaultMirrorFolder = "_FRA_Demo";

    public static DemoModeState Resolve(RuntimeConfigSnapshot snapshot, IPathCanonicalizer canonicalizer)
    {
        var mirrorFolderName = string.IsNullOrWhiteSpace(snapshot.UserPreferences.DemoMirrorFolderName)
            ? DefaultMirrorFolder
            : snapshot.UserPreferences.DemoMirrorFolderName.Trim();

        var mirrorRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in snapshot.Policy.Projects)
        {
            var liveRoot = ResolveProjectRoot(project, canonicalizer);
            if (string.IsNullOrWhiteSpace(liveRoot))
            {
                continue;
            }

            var resolvedMirrorRoot = snapshot.UserPreferences.DemoMirrorRootsByProject.TryGetValue(project.ProjectId, out var configuredMirrorRoot) &&
                                     !string.IsNullOrWhiteSpace(configuredMirrorRoot)
                ? canonicalizer.Canonicalize(configuredMirrorRoot)
                : canonicalizer.Canonicalize(Path.Combine(liveRoot, mirrorFolderName));

            mirrorRoots[project.ProjectId] = resolvedMirrorRoot;
        }

        var enabled = snapshot.UserPreferences.DemoModeEnabled && mirrorRoots.Count > 0;
        return new DemoModeState(
            enabled,
            mirrorFolderName,
            mirrorRoots,
            snapshot.UserPreferences.LastDemoMirrorRefreshUtc);
    }

    public static string ResolveProjectRoot(ProjectPolicy project, IPathCanonicalizer canonicalizer)
    {
        var matcher = project.PathMatchers.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        if (string.IsNullOrWhiteSpace(matcher))
        {
            return string.Empty;
        }

        return canonicalizer.Canonicalize(matcher).TrimEnd('\\');
    }
}
