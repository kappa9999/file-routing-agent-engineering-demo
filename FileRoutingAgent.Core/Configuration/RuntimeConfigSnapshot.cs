namespace FileRoutingAgent.Core.Configuration;

using FileRoutingAgent.Core.Domain;

public sealed class RuntimeConfigSnapshot
{
    public required string PolicyPath { get; init; }
    public required string UserPreferencesPath { get; init; }
    public required FirmPolicy Policy { get; init; }
    public required UserPreferences UserPreferences { get; init; }
    public required bool SafeModeEnabled { get; init; }
    public string? SafeModeReason { get; init; }
    public DemoModeState DemoMode { get; init; } = DemoModeState.Disabled;
}
