namespace FileRoutingAgent.Core.Configuration;

public sealed class FirmPolicy
{
    public int SchemaVersion { get; set; } = 2;
    public PolicyIntegritySettings PolicyIntegrity { get; set; } = new();
    public MonitoringSettings Monitoring { get; set; } = new();
    public List<string> ManagedExtensions { get; set; } = new();
    public IgnorePatternSettings IgnorePatterns { get; set; } = new();
    public SuppressionSettings Suppression { get; set; } = new();
    public StabilitySettings Stability { get; set; } = new();
    public ConflictPolicySettings ConflictPolicy { get; set; } = new();
    public List<ProjectPolicy> Projects { get; set; } = new();
}

public sealed class PolicyIntegritySettings
{
    public bool Required { get; set; } = true;
    public string Algorithm { get; set; } = "sha256";
    public string SignatureFile { get; set; } = "firm-policy.json.sig";
    public string PublicKeyId { get; set; } = "local";
}

public sealed class MonitoringSettings
{
    public List<string> CandidateRoots { get; set; } = new();
    public List<string> WatchRoots { get; set; } = new();
    public int ReconciliationIntervalMinutes { get; set; } = 5;
    public bool PriorityRescanOnWatcherOverflow { get; set; } = true;
    public int PromptCooldownMinutes { get; set; } = 20;
    public int RenameClusterWindowSeconds { get; set; } = 7;
}

public sealed class IgnorePatternSettings
{
    public List<string> FileGlobs { get; set; } = new();
    public List<string> FolderGlobs { get; set; } = new();
}

public sealed class SuppressionSettings
{
    public int RecentOperationTtlMinutes { get; set; } = 20;
}

public sealed class StabilitySettings
{
    public int MinAgeSeconds { get; set; } = 3;
    public int QuietSeconds { get; set; } = 8;
    public int Checks { get; set; } = 3;
    public int CheckIntervalMs { get; set; } = 1500;
    public bool RequireUnlocked { get; set; } = true;
    public bool CopySafeOpen { get; set; } = true;
}

public sealed class ConflictPolicySettings
{
    public string Mode { get; set; } = "version_then_prompt";
    public string VersionSuffixTemplate { get; set; } = "_{yyyyMMdd_HHmmss}_{user}_{machine}";
    public string DefaultChoice { get; set; } = "keep_both";
    public bool AllowOverwriteWithConfirmation { get; set; } = true;
}

public sealed class ProjectPolicy
{
    public string ProjectId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> PathMatchers { get; set; } = new();
    public List<string> WorkingRoots { get; set; } = new();
    public OfficialDestinations OfficialDestinations { get; set; } = new();
    public ProjectDefaults Defaults { get; set; } = new();
    public ExternalConnectorPolicy Connector { get; set; } = new();
}

public sealed class OfficialDestinations
{
    public string CadPublish { get; set; } = string.Empty;
    public string PlotSets { get; set; } = string.Empty;
    public Dictionary<string, string> PdfCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProjectDefaults
{
    public string PdfAction { get; set; } = "move";
    public string CadAction { get; set; } = "publish_copy";
    public string DefaultPdfCategory { get; set; } = "progress_print";
    public string OfficialDestinationMode { get; set; } = "monitor_no_prompt";
}

public sealed class ExternalConnectorPolicy
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "none";
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
