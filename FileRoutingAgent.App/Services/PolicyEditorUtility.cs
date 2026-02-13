using System.Security.Cryptography;
using System.Text.Json;
using FileRoutingAgent.Core.Configuration;
using System.IO;

namespace FileRoutingAgent.App.Services;

public static class PolicyEditorUtility
{
    private const string DefaultProjectWiseScriptPath = @"%ProgramData%\FileRoutingAgent\Connectors\ProjectWisePublish.ps1";
    private const string DefaultProjectWiseCommandArguments =
        "-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ProjectId \"{projectId}\" -SourcePath \"{sourcePath}\" -DestinationPath \"{destinationPath}\" -Action \"{action}\" -Category \"{category}\"";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static bool TryParsePolicy(string json, out FirmPolicy? policy, out string? error)
    {
        try
        {
            policy = JsonSerializer.Deserialize<FirmPolicy>(json, JsonOptions);
            if (policy is null)
            {
                error = "Unable to deserialize policy JSON.";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception exception)
        {
            policy = null;
            error = exception.Message;
            return false;
        }
    }

    public static string SerializePolicy(FirmPolicy policy)
    {
        return JsonSerializer.Serialize(policy, JsonOptions);
    }

    public static IReadOnlyList<string> ValidatePolicy(FirmPolicy policy)
    {
        var errors = new List<string>();

        if (policy.SchemaVersion <= 0)
        {
            errors.Add("SchemaVersion must be greater than 0.");
        }

        if (policy.ManagedExtensions.Count == 0)
        {
            errors.Add("ManagedExtensions must include at least one extension.");
        }

        if (policy.Projects.Count == 0)
        {
            errors.Add("At least one project must be configured.");
        }

        var duplicateProjects = policy.Projects
            .GroupBy(project => project.ProjectId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateProjects.Count > 0)
        {
            errors.Add($"Duplicate ProjectId values: {string.Join(", ", duplicateProjects)}");
        }

        foreach (var project in policy.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.ProjectId))
            {
                errors.Add("ProjectId cannot be empty.");
            }

            if (project.PathMatchers.Count == 0)
            {
                errors.Add($"Project '{project.ProjectId}' has no path matchers.");
            }

            if (string.IsNullOrWhiteSpace(project.OfficialDestinations.CadPublish))
            {
                errors.Add($"Project '{project.ProjectId}' must define official CAD publish destination.");
            }

            if (project.OfficialDestinations.PdfCategories.Count == 0)
            {
                errors.Add($"Project '{project.ProjectId}' must define at least one PDF category destination.");
            }

            if (!project.OfficialDestinations.PdfCategories.ContainsKey(project.Defaults.DefaultPdfCategory))
            {
                errors.Add(
                    $"Project '{project.ProjectId}' default PDF category '{project.Defaults.DefaultPdfCategory}' is missing from pdfCategories.");
            }

            if (project.Connector.Enabled && string.IsNullOrWhiteSpace(project.Connector.Provider))
            {
                errors.Add($"Project '{project.ProjectId}' connector provider is required when connector is enabled.");
            }

            if (project.Connector.Enabled && IsCommandProvider(project.Connector.Provider))
            {
                if (!project.Connector.Settings.TryGetValue("command", out var command) ||
                    string.IsNullOrWhiteSpace(command))
                {
                    errors.Add($"Project '{project.ProjectId}' command connector requires settings.command.");
                }
            }
        }

        return errors;
    }

    public static string ResolveSignaturePath(FirmPolicy policy, string policyPath)
    {
        var signatureFile = string.IsNullOrWhiteSpace(policy.PolicyIntegrity.SignatureFile)
            ? "firm-policy.json.sig"
            : policy.PolicyIntegrity.SignatureFile.Trim();

        if (Path.IsPathRooted(signatureFile))
        {
            return signatureFile;
        }

        var parentDirectory = Path.GetDirectoryName(policyPath) ?? AppContext.BaseDirectory;
        return Path.Combine(parentDirectory, signatureFile);
    }

    public static string ComputeSha256Hex(string policyPath)
    {
        var bytes = File.ReadAllBytes(policyPath);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }

    public static bool SignatureMatches(string policyPath, string signaturePath)
    {
        if (!File.Exists(signaturePath))
        {
            return false;
        }

        var expected = File.ReadAllText(signaturePath).Trim();
        var actual = ComputeSha256Hex(policyPath);
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    public static ProjectPolicy BuildProjectTemplate(
        string projectId,
        string displayName,
        string projectRoot,
        bool enableProjectWiseCommandProfile = false)
    {
        var root = projectRoot.Trim().TrimEnd('\\');
        return new ProjectPolicy
        {
            ProjectId = projectId.Trim(),
            DisplayName = displayName.Trim(),
            PathMatchers =
            [
                $"{root}\\"
            ],
            WorkingRoots =
            [
                Path.Combine(root, "60_CAD", "_Working"),
                Path.Combine(root, "70_Design", "_Working")
            ],
            OfficialDestinations = new OfficialDestinations
            {
                CadPublish = Path.Combine(root, "60_CAD", "Published"),
                PlotSets = Path.Combine(root, "70_Design", "90_PlotSets"),
                PdfCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["progress_print"] = Path.Combine(root, "70_Design", "10_ProgressPrints"),
                    ["exhibit"] = Path.Combine(root, "70_Design", "20_Exhibits"),
                    ["check_print"] = Path.Combine(root, "70_Design", "30_CheckPrints"),
                    ["clean_set"] = Path.Combine(root, "70_Design", "40_CleanSets")
                }
            },
            Defaults = new ProjectDefaults
            {
                PdfAction = "move",
                CadAction = "publish_copy",
                DefaultPdfCategory = "progress_print",
                OfficialDestinationMode = "monitor_no_prompt"
            },
            Connector = new ExternalConnectorPolicy
            {
                Enabled = enableProjectWiseCommandProfile,
                Provider = "projectwise_script",
                Settings = BuildProjectWiseCommandSettings()
            }
        };
    }

    public static ExternalConnectorPolicy BuildProjectWiseCommandProfile(bool enabled)
    {
        return new ExternalConnectorPolicy
        {
            Enabled = enabled,
            Provider = "projectwise_script",
            Settings = BuildProjectWiseCommandSettings()
        };
    }

    public static bool IsCommandConnectorProvider(string provider)
    {
        return IsCommandProvider(provider);
    }

    private static bool IsCommandProvider(string provider)
    {
        return provider.Equals("command", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("process", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("projectwise_script", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("projectwise_cli", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildProjectWiseCommandSettings()
    {
        var arguments = DefaultProjectWiseCommandArguments.Replace(
            "{scriptPath}",
            DefaultProjectWiseScriptPath,
            StringComparison.OrdinalIgnoreCase);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["command"] = "powershell.exe",
            ["arguments"] = arguments,
            ["timeoutSeconds"] = "120",
            ["parseStdoutJson"] = "true"
        };
    }
}
