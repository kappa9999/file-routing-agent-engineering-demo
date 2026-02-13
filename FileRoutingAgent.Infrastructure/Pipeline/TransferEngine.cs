using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class TransferEngine(
    IRootAvailabilityTracker rootAvailabilityTracker,
    ILogger<TransferEngine> logger) : ITransferEngine
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(16)
    ];

    public async Task<TransferResult> ExecuteAsync(TransferPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Conflict.Choice == ConflictChoice.Cancel ||
            plan.Decision.Action is ProposedAction.Leave or ProposedAction.None)
        {
            return new TransferResult(
                Success: false,
                plan.ClassifiedFile.File.SourcePath,
                plan.Conflict.FinalDestinationPath,
                plan.Decision.Action,
                Error: "User cancelled or left in place.",
                Attempts: 0);
        }

        var sourcePath = plan.ClassifiedFile.File.SourcePath;
        var destinationPath = plan.Conflict.FinalDestinationPath;
        var rootPath = Path.GetPathRoot(destinationPath) ?? destinationPath;

        for (var attempt = 1; attempt <= RetryDelays.Length; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureDestinationDirectory(destinationPath);

                if (!File.Exists(sourcePath))
                {
                    return new TransferResult(false, sourcePath, destinationPath, plan.Decision.Action, "Source file no longer exists.", attempt);
                }

                var shouldOverwrite = plan.Conflict.Choice == ConflictChoice.Overwrite;
                ExecuteCopyOrMove(sourcePath, destinationPath, plan.Decision.Action, shouldOverwrite);

                rootAvailabilityTracker.MarkAvailable(rootPath, "Transfer succeeded.");
                return new TransferResult(true, sourcePath, destinationPath, plan.Decision.Action, null, attempt);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                rootAvailabilityTracker.MarkUnavailable(rootPath, exception.Message);
                logger.LogWarning(
                    exception,
                    "Transfer attempt {Attempt} failed for {Source} -> {Destination}",
                    attempt,
                    sourcePath,
                    destinationPath);

                if (attempt == RetryDelays.Length)
                {
                    return new TransferResult(false, sourcePath, destinationPath, plan.Decision.Action, exception.Message, attempt);
                }

                await Task.Delay(RetryDelays[attempt - 1], cancellationToken);
                rootAvailabilityTracker.MarkRecovering(rootPath, "Retrying transfer.");
            }
        }

        return new TransferResult(false, sourcePath, destinationPath, plan.Decision.Action, "Transfer failed after retries.", RetryDelays.Length);
    }

    private static void ExecuteCopyOrMove(string sourcePath, string destinationPath, ProposedAction action, bool overwrite)
    {
        var isCopy = action is ProposedAction.Copy or ProposedAction.PublishCopy;

        if (overwrite && File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        if (isCopy)
        {
            File.Copy(sourcePath, destinationPath, overwrite: false);
            return;
        }

        if (File.Exists(destinationPath))
        {
            throw new IOException($"Destination already exists: {destinationPath}");
        }

        File.Move(sourcePath, destinationPath);
    }

    private static void EnsureDestinationDirectory(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}

