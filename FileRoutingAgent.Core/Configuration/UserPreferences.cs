namespace FileRoutingAgent.Core.Configuration;

public sealed class UserPreferences
{
    public int SchemaVersion { get; set; } = 2;
    public Dictionary<string, AutoApplyPreference> AutoApply { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DateTime> SnoozedPathsUtc { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> IgnoredFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool MonitoringPaused { get; set; }
    public DateTime? MonitoringPausedUntilUtc { get; set; }
    public bool DemoModeEnabled { get; set; }
    public string DemoMirrorFolderName { get; set; } = "_FRA_Demo";
    public Dictionary<string, string> DemoMirrorRootsByProject { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime? LastDemoMirrorRefreshUtc { get; set; }
    public string? LastProjectStructureSummary { get; set; }
}

public sealed class AutoApplyPreference
{
    public string ProjectId { get; set; } = string.Empty;
    public string CategoryKey { get; set; } = string.Empty;
    public string Action { get; set; } = "move";
}
