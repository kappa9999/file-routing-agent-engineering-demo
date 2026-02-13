namespace FileRoutingAgent.Core.Configuration;

public sealed class AgentRuntimeOptions
{
    public string PolicyPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "Config", "firm-policy.json");
    public string UserPreferencesPath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileRoutingAgent", "user-preferences.json");
    public string DatabasePath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileRoutingAgent", "state.db");
}

