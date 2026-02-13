using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class ConflictResolver(
    IUserPromptService userPromptService,
    RuntimeConfigSnapshotAccessor snapshotAccessor,
    IPathCanonicalizer canonicalizer,
    ILogger<ConflictResolver> logger) : IConflictResolver
{
    public async Task<ConflictPlan> BuildPlanAsync(
        ClassifiedFile file,
        RouteResult route,
        ProjectPolicy project,
        UserDecision decision,
        CancellationToken cancellationToken)
    {
        var destinationPath = canonicalizer.Canonicalize(route.DestinationPath);
        var validationErrors = ValidateDestination(destinationPath);
        if (validationErrors.Count > 0)
        {
            var suggestedPath = BuildSanitizedPath(destinationPath);
            var invalidChoice = await userPromptService.PromptForInvalidDestinationAsync(
                validationErrors,
                file.File.SourcePath,
                suggestedPath,
                cancellationToken);

            if (invalidChoice == ConflictChoice.Cancel)
            {
                return new ConflictPlan(destinationPath, false, null, ConflictChoice.Cancel, validationErrors);
            }

            destinationPath = suggestedPath;
        }

        var exists = File.Exists(destinationPath);
        if (!exists)
        {
            return new ConflictPlan(destinationPath, false, null, ConflictChoice.KeepBothVersioned, validationErrors);
        }

        var plan = new TransferPlan(
            file,
            new ProjectResolution(project.ProjectId, project.DisplayName, null),
            decision,
            route,
            new ConflictPlan(destinationPath, true, destinationPath, ConflictChoice.KeepBothVersioned, validationErrors));

        var choice = await userPromptService.PromptForConflictAsync(plan, cancellationToken);
        if (choice == ConflictChoice.Cancel)
        {
            return new ConflictPlan(destinationPath, true, destinationPath, choice, validationErrors);
        }

        if (choice == ConflictChoice.Overwrite)
        {
            return new ConflictPlan(destinationPath, true, destinationPath, choice, validationErrors);
        }

        var versioned = BuildVersionedDestination(destinationPath, snapshotAccessor.Snapshot?.Policy.ConflictPolicy.VersionSuffixTemplate);
        logger.LogInformation("Conflict resolved with versioned destination: {VersionedDestination}", versioned);
        return new ConflictPlan(versioned, true, destinationPath, ConflictChoice.KeepBothVersioned, validationErrors);
    }

    private static List<ConflictValidationError> ValidateDestination(string path)
    {
        var errors = new List<ConflictValidationError>();

        var fileName = Path.GetFileName(path);
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            errors.Add(new ConflictValidationError("invalid_chars", "Destination file name contains invalid characters."));
        }

        if (path.Length > 32000)
        {
            errors.Add(new ConflictValidationError("path_too_long", "Destination path exceeds long path limit."));
        }

        if (path.Contains("..\\", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ConflictValidationError("path_traversal", "Destination path contains traversal segments."));
        }

        return errors;
    }

    private static string BuildSanitizedPath(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var extension = Path.GetExtension(path);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitizedName = new string(fileNameWithoutExtension.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return Path.Combine(directory, $"{sanitizedName}{extension}");
    }

    private static string BuildVersionedDestination(string destinationPath, string? template)
    {
        var directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        var extension = Path.GetExtension(destinationPath);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(destinationPath);

        var suffix = (template ?? "_{yyyyMMdd_HHmmss}_{user}_{machine}")
            .Replace("{yyyyMMdd_HHmmss}", DateTime.Now.ToString("yyyyMMdd_HHmmss"))
            .Replace("{user}", Environment.UserName)
            .Replace("{machine}", Environment.MachineName);

        return Path.Combine(directory, $"{fileNameWithoutExtension}{suffix}{extension}");
    }
}
