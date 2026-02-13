using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;

namespace FileRoutingAgent.Core.Utilities;

public static class PolicyExtensions
{
    public static IEnumerable<string> Expand(this IEnumerable<string> values)
    {
        return values.Select(Environment.ExpandEnvironmentVariables);
    }

    public static string Expand(this string value)
    {
        return Environment.ExpandEnvironmentVariables(value);
    }

    public static ProposedAction ToDefaultAction(this ProjectPolicy project, FileCategory category)
    {
        var raw = category == FileCategory.Pdf ? project.Defaults.PdfAction : project.Defaults.CadAction;
        return raw.Trim().ToLowerInvariant() switch
        {
            "move" => ProposedAction.Move,
            "copy" => ProposedAction.Copy,
            "publish_copy" => ProposedAction.PublishCopy,
            "publishcopy" => ProposedAction.PublishCopy,
            "leave" => ProposedAction.Leave,
            _ => category == FileCategory.Cad ? ProposedAction.PublishCopy : ProposedAction.Move
        };
    }

    public static ConflictChoice ToDefaultConflictChoice(this ConflictPolicySettings settings)
    {
        return settings.DefaultChoice.Trim().ToLowerInvariant() switch
        {
            "overwrite" => ConflictChoice.Overwrite,
            "cancel" => ConflictChoice.Cancel,
            _ => ConflictChoice.KeepBothVersioned
        };
    }
}

