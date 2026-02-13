namespace FileRoutingAgent.Core.Configuration;

public sealed class UserPreferences
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, AutoApplyPreference> AutoApply { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DateTime> SnoozedPathsUtc { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> IgnoredFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool MonitoringPaused { get; set; }
    public DateTime? MonitoringPausedUntilUtc { get; set; }
}

public sealed class AutoApplyPreference
{
    public string ProjectId { get; set; } = string.Empty;
    public string CategoryKey { get; set; } = string.Empty;
    public string Action { get; set; } = "move";
}

