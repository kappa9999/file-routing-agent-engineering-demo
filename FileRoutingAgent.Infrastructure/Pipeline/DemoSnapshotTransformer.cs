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

            mirrorRoot = canonicalizer.Canonicalize(mirrorRoot);
            var liveRoot = DemoModeStateFactory.ResolveProjectRoot(project, canonicalizer);
            if (string.IsNullOrWhiteSpace(liveRoot))
            {
                continue;
            }

            project.PathMatchers = project.PathMatchers
                .Select(path => TryMapPath(path, liveRoot, mirrorRoot, canonicalizer, out var mapped) ? mapped : string.Empty)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (project.PathMatchers.Count == 0)
            {
                project.PathMatchers = [mirrorRoot];
            }

            project.WorkingRoots = project.WorkingRoots
                .Select(path => TryMapPath(path, liveRoot, mirrorRoot, canonicalizer, out var mapped) ? mapped : string.Empty)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            project.OfficialDestinations.CadPublish = TryMapPath(
                project.OfficialDestinations.CadPublish,
                liveRoot,
                mirrorRoot,
                canonicalizer,
                out var mappedCadPublish)
                ? mappedCadPublish
                : string.Empty;
            project.OfficialDestinations.PlotSets = TryMapPath(
                project.OfficialDestinations.PlotSets,
                liveRoot,
                mirrorRoot,
                canonicalizer,
                out var mappedPlotSets)
                ? mappedPlotSets
                : string.Empty;

            var remappedPdfCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (category, destination) in project.OfficialDestinations.PdfCategories)
            {
                if (TryMapPath(destination, liveRoot, mirrorRoot, canonicalizer, out var mappedDestination) &&
                    !string.IsNullOrWhiteSpace(mappedDestination))
                {
                    remappedPdfCategories[category] = mappedDestination;
                }
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

    private static bool TryMapPath(
        string path,
        string liveRoot,
        string mirrorRoot,
        IPathCanonicalizer canonicalizer,
        out string mappedPath)
    {
        mappedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var canonicalPath = canonicalizer.Canonicalize(path);
        var canonicalLiveRoot = canonicalizer.Canonicalize(liveRoot).TrimEnd('\\');
        var canonicalMirrorRoot = canonicalizer.Canonicalize(mirrorRoot).TrimEnd('\\');

        if (!canonicalizer.PathStartsWith(canonicalPath, canonicalLiveRoot))
        {
            return false;
        }

        var relativePath = canonicalPath.Length == canonicalLiveRoot.Length
            ? string.Empty
            : canonicalPath[canonicalLiveRoot.Length..].TrimStart('\\');

        mappedPath = string.IsNullOrWhiteSpace(relativePath)
            ? canonicalMirrorRoot
            : canonicalizer.Canonicalize(Path.Combine(canonicalMirrorRoot, relativePath));
        return true;
    }

    private static FirmPolicy ClonePolicy(FirmPolicy policy)
    {
        var json = JsonSerializer.Serialize(policy, JsonOptions);
        return JsonSerializer.Deserialize<FirmPolicy>(json, JsonOptions)
               ?? throw new InvalidOperationException("Failed to clone policy.");
    }
}
