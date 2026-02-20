using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class DemoSafetyGuard(
    IPathCanonicalizer canonicalizer) : IDemoSafetyGuard
{
    public bool IsAllowed(TransferPlan plan, DemoModeState state, out string reason)
    {
        if (!state.Enabled)
        {
            reason = string.Empty;
            return true;
        }

        if (state.ProjectMirrorRoots.Count == 0)
        {
            reason = "Demo mode is enabled but no mirror roots are configured.";
            return false;
        }

        if (!IsPathInMirrorScope(plan.ClassifiedFile.File.SourcePath, state, canonicalizer))
        {
            reason = "Source path is outside demo mirror scope.";
            return false;
        }

        if (!IsPathInMirrorScope(plan.Conflict.FinalDestinationPath, state, canonicalizer))
        {
            reason = "Destination path is outside demo mirror scope.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool IsPathInMirrorScope(string path, DemoModeState state, IPathCanonicalizer pathCanonicalizer)
    {
        if (!state.Enabled)
        {
            return true;
        }

        var canonicalPath = pathCanonicalizer.Canonicalize(path);
        return state.ProjectMirrorRoots.Values.Any(root => pathCanonicalizer.PathStartsWith(canonicalPath, root));
    }
}
