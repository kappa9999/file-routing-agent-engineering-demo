using System.Text.Json;
using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class DemoSnapshotTransformer : IDemoSnapshotTransformer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public RuntimeConfigSnapshot ApplyDemoOverlay(
        RuntimeConfigSnapshot baseSnapshot,
        DemoModeState state,
        IPathCanonicalizer canonicalizer)
    {
        if (!state.Enabled || state.ProjectMirrorRoots.Count == 0)
        {
            return new RuntimeConfigSnapshot
            {
                PolicyPath = baseSnapshot.PolicyPath,
                UserPreferencesPath = baseSnapshot.UserPreferencesPath,
                Policy = ClonePolicy(baseSnapshot.Policy),
                UserPreferences = baseSnapshot.UserPreferences,
                SafeModeEnabled = baseSnapshot.SafeModeEnabled,
                SafeModeReason = baseSnapshot.SafeModeReason,
                DemoMode = state
            };
        }

        var policy = ClonePolicy(baseSnapshot.Policy);
        foreach (var project in policy.Projects)
        {
            if (!state.ProjectMirrorRoots.TryGetValue(project.ProjectId, out var mirrorRoot) ||
                string.IsNullOrWhiteSpace(mirrorRoot))
            {
                continue;
            }

            var liveRoot = DemoModeStateFactory.ResolveProjectRoot(project, canonicalizer);
            if (string.IsNullOrWhiteSpace(liveRoot))
            {
                continue;
            }

            project.PathMatchers = project.PathMatchers
                .Select(path => MapPath(path, liveRoot, mirrorRoot, canonicalizer))
                .ToList();

            project.WorkingRoots = project.WorkingRoots
                .Select(path => MapPath(path, liveRoot, mirrorRoot, canonicalizer))
                .ToList();

            project.OfficialDestinations.CadPublish = MapPath(project.OfficialDestinations.CadPublish, liveRoot, mirrorRoot, canonicalizer);
            project.OfficialDestinations.PlotSets = MapPath(project.OfficialDestinations.PlotSets, liveRoot, mirrorRoot, canonicalizer);

            var remappedPdfCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (category, destination) in project.OfficialDestinations.PdfCategories)
            {
                remappedPdfCategories[category] = MapPath(destination, liveRoot, mirrorRoot, canonicalizer);
            }
            project.OfficialDestinations.PdfCategories = remappedPdfCategories;
        }

        policy.Monitoring.CandidateRoots = policy.Projects
            .SelectMany(project => project.WorkingRoots)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        policy.Monitoring.WatchRoots = state.ProjectMirrorRoots.Values
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RuntimeConfigSnapshot
        {
            PolicyPath = baseSnapshot.PolicyPath,
            UserPreferencesPath = baseSnapshot.UserPreferencesPath,
            Policy = policy,
            UserPreferences = baseSnapshot.UserPreferences,
            SafeModeEnabled = baseSnapshot.SafeModeEnabled,
            SafeModeReason = baseSnapshot.SafeModeReason,
            DemoMode = state
        };
    }

    private static string MapPath(
        string path,
        string liveRoot,
        string mirrorRoot,
        IPathCanonicalizer canonicalizer)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var canonicalPath = canonicalizer.Canonicalize(path);
        var canonicalLiveRoot = canonicalizer.Canonicalize(liveRoot).TrimEnd('\\');
        var canonicalMirrorRoot = canonicalizer.Canonicalize(mirrorRoot).TrimEnd('\\');

        if (!canonicalizer.PathStartsWith(canonicalPath, canonicalLiveRoot))
        {
            return canonicalPath;
        }

        var relativePath = canonicalPath.Length == canonicalLiveRoot.Length
            ? string.Empty
            : canonicalPath[canonicalLiveRoot.Length..].TrimStart('\\');

        return string.IsNullOrWhiteSpace(relativePath)
            ? canonicalMirrorRoot
            : canonicalizer.Canonicalize(Path.Combine(canonicalMirrorRoot, relativePath));
    }

    private static FirmPolicy ClonePolicy(FirmPolicy policy)
    {
        var json = JsonSerializer.Serialize(policy, JsonOptions);
        return JsonSerializer.Deserialize<FirmPolicy>(json, JsonOptions)
               ?? throw new InvalidOperationException("Failed to clone policy.");
    }
}
