using System.Text.Json;
using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileRoutingAgent.Infrastructure.Configuration;

public sealed class PolicyConfigManager(
    IOptions<AgentRuntimeOptions> runtimeOptions,
    IPolicyIntegrityGuard integrityGuard,
    ILogger<PolicyConfigManager> logger) : IPolicyConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<RuntimeConfigSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var options = runtimeOptions.Value;
        EnsureParentDirectory(options.UserPreferencesPath);
        EnsureParentDirectory(options.PolicyPath);

        if (!File.Exists(options.PolicyPath))
        {
            throw new FileNotFoundException("Policy file is missing.", options.PolicyPath);
        }

        var policyJson = await File.ReadAllTextAsync(options.PolicyPath, cancellationToken);
        var policy = JsonSerializer.Deserialize<FirmPolicy>(policyJson, JsonOptions)
            ?? throw new InvalidOperationException("Unable to deserialize firm policy.");

        NormalizePolicy(policy);

        var integrityResult = await integrityGuard.VerifyAsync(options.PolicyPath, policy.PolicyIntegrity, cancellationToken);
        if (!integrityResult.IsValid)
        {
            logger.LogError("Policy integrity validation failed: {Reason}", integrityResult.Error);
        }

        var userPreferences = await LoadUserPreferencesAsync(options.UserPreferencesPath, cancellationToken);

        return new RuntimeConfigSnapshot
        {
            PolicyPath = options.PolicyPath,
            UserPreferencesPath = options.UserPreferencesPath,
            Policy = policy,
            UserPreferences = userPreferences,
            SafeModeEnabled = !integrityResult.IsValid,
            SafeModeReason = integrityResult.Error
        };
    }

    public async Task SaveUserPreferencesAsync(UserPreferences preferences, CancellationToken cancellationToken)
    {
        var options = runtimeOptions.Value;
        EnsureParentDirectory(options.UserPreferencesPath);

        var json = JsonSerializer.Serialize(preferences, JsonOptions);
        await File.WriteAllTextAsync(options.UserPreferencesPath, json, cancellationToken);
    }

    private static async Task<UserPreferences> LoadUserPreferencesAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new UserPreferences();
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions) ?? new UserPreferences();
    }

    private static void NormalizePolicy(FirmPolicy policy)
    {
        policy.ManagedExtensions = policy.ManagedExtensions
            .Where(ext => !string.IsNullOrWhiteSpace(ext))
            .Select(ext => ext.Trim().StartsWith('.') ? ext.Trim().ToLowerInvariant() : $".{ext.Trim().ToLowerInvariant()}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        policy.Monitoring.CandidateRoots = policy.Monitoring.CandidateRoots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Environment.ExpandEnvironmentVariables(path.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        policy.Monitoring.WatchRoots = policy.Monitoring.WatchRoots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Environment.ExpandEnvironmentVariables(path.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var project in policy.Projects)
        {
            project.PathMatchers = project.PathMatchers
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Environment.ExpandEnvironmentVariables(path.Trim()))
                .ToList();

            project.WorkingRoots = project.WorkingRoots
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Environment.ExpandEnvironmentVariables(path.Trim()))
                .ToList();

            project.OfficialDestinations.CadPublish = Environment.ExpandEnvironmentVariables(project.OfficialDestinations.CadPublish ?? string.Empty);
            project.OfficialDestinations.PlotSets = Environment.ExpandEnvironmentVariables(project.OfficialDestinations.PlotSets ?? string.Empty);

            var normalizedCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, path) in project.OfficialDestinations.PdfCategories)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                normalizedCategories[key.Trim()] = Environment.ExpandEnvironmentVariables(path.Trim());
            }

            project.OfficialDestinations.PdfCategories = normalizedCategories;
        }
    }

    private static void EnsureParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parent))
        {
            return;
        }

        Directory.CreateDirectory(parent);
    }
}

