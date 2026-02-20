using System.IO;
using System.IO.Compression;
using System.Text.Json;
using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileRoutingAgent.App.Services;

public sealed class SupportBundleService(
    IAuditStore auditStore,
    RuntimeConfigSnapshotAccessor snapshotAccessor,
    IOptions<AgentRuntimeOptions> runtimeOptions,
    ILogger<SupportBundleService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<SupportBundleResult> CreateBundleAsync(CancellationToken cancellationToken)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var outputPath = Path.Combine(desktop, $"FileRoutingAgent_Support_{timestamp}.zip");
        var tempRoot = Path.Combine(Path.GetTempPath(), $"FileRoutingAgent_Support_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var warnings = new List<string>();
        var includedFiles = new List<string>();

        try
        {
            var snapshot = snapshotAccessor.Snapshot;
            await WriteSummaryFilesAsync(tempRoot, snapshot, cancellationToken);

            var policyPath = snapshot?.PolicyPath ?? runtimeOptions.Value.PolicyPath;
            var signaturePath = snapshot?.Policy is not null
                ? PolicyEditorUtility.ResolveSignaturePath(snapshot.Policy, policyPath)
                : $"{policyPath}.sig";
            var userPreferencesPath = snapshot?.UserPreferencesPath ?? runtimeOptions.Value.UserPreferencesPath;
            var databasePath = runtimeOptions.Value.DatabasePath;
            var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

            CopyIfExists(policyPath, tempRoot, Path.Combine("policy", "firm-policy.json"), warnings, includedFiles);
            CopyIfExists(signaturePath, tempRoot, Path.Combine("policy", "firm-policy.json.sig"), warnings, includedFiles);
            CopyIfExists(userPreferencesPath, tempRoot, Path.Combine("preferences", "user-preferences.json"), warnings, includedFiles);
            CopyIfExists(databasePath, tempRoot, Path.Combine("state", "state.db"), warnings, includedFiles);
            CopyIfExists($"{databasePath}-wal", tempRoot, Path.Combine("state", "state.db-wal"), warnings, includedFiles);
            CopyIfExists($"{databasePath}-shm", tempRoot, Path.Combine("state", "state.db-shm"), warnings, includedFiles);
            CopyIfExists(appSettingsPath, tempRoot, Path.Combine("app", "appsettings.json"), warnings, includedFiles);

            var logRoot = Path.Combine(Path.GetDirectoryName(databasePath) ?? string.Empty, "Logs");
            CopyLatestLogs(logRoot, tempRoot, "logs", warnings, includedFiles);

            var manifest = new
            {
                generatedAtUtc = DateTime.UtcNow,
                includedFileCount = includedFiles.Count,
                warningCount = warnings.Count,
                includedFiles,
                warnings
            };
            await WriteJsonAsync(Path.Combine(tempRoot, "summary", "bundle-manifest.json"), manifest, cancellationToken);
            includedFiles.Add(Path.Combine("summary", "bundle-manifest.json"));

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            ZipFile.CreateFromDirectory(tempRoot, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            logger.LogInformation("Support bundle exported: {OutputPath}", outputPath);
            return new SupportBundleResult(outputPath, includedFiles, warnings);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private async Task WriteSummaryFilesAsync(
        string tempRoot,
        RuntimeConfigSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        var summaryDir = Path.Combine(tempRoot, "summary");
        Directory.CreateDirectory(summaryDir);

        var events = await auditStore.GetRecentAuditEventsAsync(800, cancellationToken);
        var scans = await auditStore.GetRecentScanRunsAsync(200, cancellationToken);

        var setupEvents = events
            .Where(entry => entry.EventType.StartsWith("easy_setup", StringComparison.OrdinalIgnoreCase) ||
                            entry.EventType.StartsWith("policy_", StringComparison.OrdinalIgnoreCase))
            .Take(200)
            .ToList();

        var summary = new
        {
            generatedAtUtc = DateTime.UtcNow,
            machine = Environment.MachineName,
            user = Environment.UserName,
            os = Environment.OSVersion.ToString(),
            dotnet = Environment.Version.ToString(),
            appBaseDirectory = AppContext.BaseDirectory,
            policyPath = snapshot?.PolicyPath,
            userPreferencesPath = snapshot?.UserPreferencesPath,
            safeModeEnabled = snapshot?.SafeModeEnabled ?? false,
            safeModeReason = snapshot?.SafeModeReason,
            projectCount = snapshot?.Policy.Projects.Count ?? 0,
            demoModeEnabled = snapshot?.DemoMode.Enabled ?? false,
            demoMirrorFolderName = snapshot?.UserPreferences.DemoMirrorFolderName,
            demoMirrorRoots = snapshot?.DemoMode.ProjectMirrorRoots,
            lastDemoMirrorRefreshUtc = snapshot?.UserPreferences.LastDemoMirrorRefreshUtc,
            lastProjectStructureSummary = snapshot?.UserPreferences.LastProjectStructureSummary,
            recentAuditEventCount = events.Count,
            recentScanRunCount = scans.Count
        };

        await WriteJsonAsync(Path.Combine(summaryDir, "support-summary.json"), summary, cancellationToken);
        await WriteJsonAsync(Path.Combine(summaryDir, "recent-audit-events.json"), events, cancellationToken);
        await WriteJsonAsync(Path.Combine(summaryDir, "recent-scan-runs.json"), scans, cancellationToken);
        await WriteJsonAsync(Path.Combine(summaryDir, "setup-events.json"), setupEvents, cancellationToken);
    }

    private static async Task WriteJsonAsync(string path, object payload, CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static void CopyIfExists(
        string sourcePath,
        string tempRoot,
        string archiveRelativePath,
        ICollection<string> warnings,
        ICollection<string> includedFiles)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                warnings.Add($"Missing file: {sourcePath}");
                return;
            }

            var destinationPath = Path.Combine(tempRoot, archiveRelativePath);
            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(sourcePath, destinationPath, overwrite: true);
            includedFiles.Add(archiveRelativePath);
        }
        catch (Exception exception)
        {
            warnings.Add($"Failed to copy '{sourcePath}': {exception.Message}");
        }
    }

    private static void CopyLatestLogs(
        string logRoot,
        string tempRoot,
        string destinationRelativePath,
        ICollection<string> warnings,
        ICollection<string> includedFiles)
    {
        try
        {
            if (!Directory.Exists(logRoot))
            {
                warnings.Add($"Log folder not found: {logRoot}");
                return;
            }

            var destinationRoot = Path.Combine(tempRoot, destinationRelativePath);
            Directory.CreateDirectory(destinationRoot);

            var files = Directory.GetFiles(logRoot, "*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => File.GetLastWriteTimeUtc(file))
                .Take(15)
                .ToList();

            foreach (var sourceFile in files)
            {
                var destinationFile = Path.Combine(destinationRoot, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, destinationFile, overwrite: true);
                includedFiles.Add(Path.Combine(destinationRelativePath, Path.GetFileName(sourceFile)));
            }
        }
        catch (Exception exception)
        {
            warnings.Add($"Failed to collect logs: {exception.Message}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }
}

public sealed record SupportBundleResult(
    string BundlePath,
    IReadOnlyCollection<string> IncludedFiles,
    IReadOnlyCollection<string> Warnings);
