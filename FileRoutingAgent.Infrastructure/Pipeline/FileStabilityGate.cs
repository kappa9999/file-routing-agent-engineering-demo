using System.Security.Cryptography;
using System.Text;
using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class FileStabilityGate(ILogger<FileStabilityGate> logger) : IFileStabilityGate
{
    public async Task<StableFile?> WaitForStableAsync(
        DetectionCandidate candidate,
        StabilitySettings settings,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(candidate.SourcePath))
        {
            return null;
        }

        var requiredStableChecks = Math.Max(1, settings.Checks);
        var checkInterval = TimeSpan.FromMilliseconds(Math.Max(100, settings.CheckIntervalMs));
        var quietWindow = TimeSpan.FromSeconds(Math.Max(1, settings.QuietSeconds));
        var minAge = TimeSpan.FromSeconds(Math.Max(0, settings.MinAgeSeconds));
        var maxWait = minAge + quietWindow + TimeSpan.FromTicks(checkInterval.Ticks * (requiredStableChecks + 4L));
        var deadlineUtc = DateTime.UtcNow + maxWait;

        long? previousSize = null;
        DateTime? previousWriteUtc = null;
        DateTime? stableSinceUtc = null;
        var stableChecks = 0;

        while (DateTime.UtcNow <= deadlineUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(candidate.SourcePath))
            {
                return null;
            }

            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(candidate.SourcePath);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Unable to inspect file {Path}", candidate.SourcePath);
                await Task.Delay(checkInterval, cancellationToken);
                continue;
            }

            if (!fileInfo.Exists)
            {
                return null;
            }

            if (DateTime.UtcNow - fileInfo.LastWriteTimeUtc < minAge)
            {
                stableChecks = 0;
                stableSinceUtc = null;
                await Task.Delay(checkInterval, cancellationToken);
                continue;
            }

            if (settings.CopySafeOpen && !CanOpenCopySafe(candidate.SourcePath))
            {
                stableChecks = 0;
                stableSinceUtc = null;
                await Task.Delay(checkInterval, cancellationToken);
                continue;
            }

            if (settings.RequireUnlocked && IsLocked(candidate.SourcePath))
            {
                stableChecks = 0;
                stableSinceUtc = null;
                await Task.Delay(checkInterval, cancellationToken);
                continue;
            }

            if (previousSize.HasValue &&
                previousWriteUtc.HasValue &&
                previousSize.Value == fileInfo.Length &&
                previousWriteUtc.Value == fileInfo.LastWriteTimeUtc)
            {
                stableChecks++;
                stableSinceUtc ??= DateTime.UtcNow;
            }
            else
            {
                stableChecks = 1;
                stableSinceUtc = DateTime.UtcNow;
                previousSize = fileInfo.Length;
                previousWriteUtc = fileInfo.LastWriteTimeUtc;
            }

            if (stableChecks >= requiredStableChecks && stableSinceUtc.HasValue)
            {
                if (DateTime.UtcNow - stableSinceUtc.Value >= quietWindow)
                {
                    return BuildStableFile(candidate.SourcePath, fileInfo.Length, fileInfo.LastWriteTimeUtc);
                }

                if (quietWindow <= TimeSpan.Zero)
                {
                    return BuildStableFile(candidate.SourcePath, fileInfo.Length, fileInfo.LastWriteTimeUtc);
                }
            }

            await Task.Delay(checkInterval, cancellationToken);
        }

        logger.LogDebug("File did not reach stable state before timeout: {Path}", candidate.SourcePath);
        return null;
    }

    private static bool CanOpenCopySafe(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[Math.Min(4096, (int)Math.Max(1, stream.Length))];
            _ = stream.Read(buffer, 0, buffer.Length);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLocked(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static StableFile BuildStableFile(string sourcePath, long sizeBytes, DateTime lastWriteUtc)
    {
        var fingerprintSource = $"{sourcePath.ToLowerInvariant()}|{sizeBytes}|{lastWriteUtc.Ticks}";
        var fingerprintBytes = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintSource));
        var fingerprint = Convert.ToHexString(fingerprintBytes);

        return new StableFile(sourcePath, sizeBytes, lastWriteUtc, fingerprint);
    }
}
