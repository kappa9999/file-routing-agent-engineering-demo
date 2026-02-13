using System.Security.Cryptography;
using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Interfaces;

namespace FileRoutingAgent.Infrastructure.Configuration;

public sealed class PolicyIntegrityGuard : IPolicyIntegrityGuard
{
    public async Task<PolicyIntegrityResult> VerifyAsync(
        string policyPath,
        PolicyIntegritySettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.Required)
        {
            return new PolicyIntegrityResult(true, null);
        }

        if (!File.Exists(policyPath))
        {
            return new PolicyIntegrityResult(false, $"Policy file not found: {policyPath}");
        }

        var signaturePath = Path.IsPathRooted(settings.SignatureFile)
            ? settings.SignatureFile
            : Path.Combine(Path.GetDirectoryName(policyPath) ?? AppContext.BaseDirectory, settings.SignatureFile);

        if (!File.Exists(signaturePath))
        {
            return new PolicyIntegrityResult(false, $"Signature file not found: {signaturePath}");
        }

        var algorithm = settings.Algorithm.Trim().ToLowerInvariant();
        if (algorithm != "sha256")
        {
            return new PolicyIntegrityResult(false, $"Unsupported policy integrity algorithm: {settings.Algorithm}");
        }

        var expectedHash = (await File.ReadAllTextAsync(signaturePath, cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return new PolicyIntegrityResult(false, "Signature file is empty.");
        }

        var policyBytes = await File.ReadAllBytesAsync(policyPath, cancellationToken);
        var actualHashBytes = SHA256.HashData(policyBytes);
        var actualHash = Convert.ToHexString(actualHashBytes);

        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            return new PolicyIntegrityResult(
                false,
                $"Policy signature mismatch. expected={expectedHash}, actual={actualHash}");
        }

        return new PolicyIntegrityResult(true, null);
    }
}

