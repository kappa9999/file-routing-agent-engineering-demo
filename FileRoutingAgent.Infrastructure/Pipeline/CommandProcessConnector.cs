using System.Diagnostics;
using System.Text.Json;
using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class CommandProcessConnector(ILogger<CommandProcessConnector> logger) : IExternalSystemConnector
{
    public string Name => "command_process";

    public bool CanHandle(ProjectPolicy project)
    {
        if (!project.Connector.Enabled)
        {
            return false;
        }

        var provider = project.Connector.Provider.Trim();
        return provider.Equals("command", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("process", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("projectwise_script", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("projectwise_cli", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ConnectorPublishResult> PublishAsync(ConnectorPublishRequest request, CancellationToken cancellationToken)
    {
        if (!request.Project.Connector.Settings.TryGetValue("command", out var command) ||
            string.IsNullOrWhiteSpace(command))
        {
            return new ConnectorPublishResult(
                Success: false,
                Status: "config_error",
                Error: "Missing connector setting 'command'.");
        }

        command = Environment.ExpandEnvironmentVariables(command.Trim());
        var argumentsTemplate = request.Project.Connector.Settings.TryGetValue("arguments", out var configuredArguments)
            ? configuredArguments
            : string.Empty;
        var arguments = Environment.ExpandEnvironmentVariables(ExpandTemplate(argumentsTemplate, request));

        var timeoutSeconds = ParseIntSetting(request.Project.Connector.Settings, "timeoutSeconds", 60, min: 1, max: 600);
        var parseStdoutJson = ParseBoolSetting(request.Project.Connector.Settings, "parseStdoutJson", defaultValue: false);
        var workingDirectory = request.Project.Connector.Settings.TryGetValue("workingDirectory", out var configuredWorkingDirectory)
            ? configuredWorkingDirectory
            : null;
        workingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? null
            : Environment.ExpandEnvironmentVariables(workingDirectory.Trim());

        var processStartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            processStartInfo.WorkingDirectory = workingDirectory;
        }

        AddConnectorEnvironment(processStartInfo, request);

        using var process = new Process
        {
            StartInfo = processStartInfo
        };

        try
        {
            process.Start();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to start connector command: {Command}", command);
            return new ConnectorPublishResult(
                Success: false,
                Status: "start_failed",
                Error: exception.Message);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            var timedOutStdout = await stdoutTask;
            var timedOutStderr = await stderrTask;
            return new ConnectorPublishResult(
                Success: false,
                Status: "timeout",
                Error: $"Connector command timed out after {timeoutSeconds} seconds.",
                Metadata: BuildMetadata(process, timedOutStdout, timedOutStderr));
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var metadata = BuildMetadata(process, stdout, stderr);

        if (parseStdoutJson && !string.IsNullOrWhiteSpace(stdout))
        {
            MergeJsonMetadata(metadata, stdout);
        }

        if (process.ExitCode != 0)
        {
            return new ConnectorPublishResult(
                Success: false,
                Status: "failed",
                Error: $"Connector command exited with code {process.ExitCode}.",
                Metadata: metadata);
        }

        return new ConnectorPublishResult(
            Success: true,
            Status: "completed",
            ExternalTransactionId: metadata.TryGetValue("externalTransactionId", out var externalId) ? externalId : null,
            Metadata: metadata);
    }

    private static string ExpandTemplate(string template, ConnectorPublishRequest request)
    {
        var sourcePath = request.File.File.SourcePath;
        var destinationPath = request.DestinationPath;
        var fileName = Path.GetFileName(sourcePath);
        var category = request.File.Category.ToString();

        return template
            .Replace("{projectId}", request.Project.ProjectId, StringComparison.OrdinalIgnoreCase)
            .Replace("{sourcePath}", sourcePath, StringComparison.OrdinalIgnoreCase)
            .Replace("{destinationPath}", destinationPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{fileName}", fileName, StringComparison.OrdinalIgnoreCase)
            .Replace("{action}", request.Action.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{category}", category, StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseIntSetting(
        IReadOnlyDictionary<string, string> settings,
        string key,
        int defaultValue,
        int min,
        int max)
    {
        if (!settings.TryGetValue(key, out var raw) || !int.TryParse(raw, out var parsed))
        {
            return defaultValue;
        }

        return Math.Clamp(parsed, min, max);
    }

    private static bool ParseBoolSetting(IReadOnlyDictionary<string, string> settings, string key, bool defaultValue)
    {
        if (!settings.TryGetValue(key, out var raw))
        {
            return defaultValue;
        }

        return raw.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddConnectorEnvironment(ProcessStartInfo processStartInfo, ConnectorPublishRequest request)
    {
        processStartInfo.Environment["FRA_PROJECT_ID"] = request.Project.ProjectId;
        processStartInfo.Environment["FRA_SOURCE_PATH"] = request.File.File.SourcePath;
        processStartInfo.Environment["FRA_DESTINATION_PATH"] = request.DestinationPath;
        processStartInfo.Environment["FRA_FILE_CATEGORY"] = request.File.Category.ToString();
        processStartInfo.Environment["FRA_ACTION"] = request.Action.ToString();
        processStartInfo.Environment["FRA_FILE_NAME"] = Path.GetFileName(request.File.File.SourcePath);
    }

    private static Dictionary<string, string> BuildMetadata(Process process, string stdout, string stderr)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["exitCode"] = process.ExitCode.ToString()
        };

        var trimmedStdout = Truncate(stdout?.Trim(), 4000);
        if (!string.IsNullOrWhiteSpace(trimmedStdout))
        {
            metadata["stdout"] = trimmedStdout;
        }

        var trimmedStderr = Truncate(stderr?.Trim(), 4000);
        if (!string.IsNullOrWhiteSpace(trimmedStderr))
        {
            metadata["stderr"] = trimmedStderr;
        }

        return metadata;
    }

    private static void MergeJsonMetadata(IDictionary<string, string> metadata, string stdout)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                metadata[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
                    JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
                    _ => property.Value.GetRawText()
                };
            }
        }
        catch
        {
            // Ignore parse failures and keep raw stdout/stderr metadata.
        }
    }

    private static string Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Length <= maxChars
            ? text
            : $"{text[..maxChars]}...(truncated)";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore kill failures; caller handles timeout status.
        }
    }
}
