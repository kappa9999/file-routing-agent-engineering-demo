namespace FileRoutingAgent.Core.Configuration;

public sealed class RuntimeConfigSnapshot
{
    public required string PolicyPath { get; init; }
    public required string UserPreferencesPath { get; init; }
    public required FirmPolicy Policy { get; init; }
    public required UserPreferences UserPreferences { get; init; }
    public required bool SafeModeEnabled { get; init; }
    public string? SafeModeReason { get; init; }
}

