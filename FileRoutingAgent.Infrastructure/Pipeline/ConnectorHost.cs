using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class ConnectorHost(
    IEnumerable<IExternalSystemConnector> connectors,
    ILogger<ConnectorHost> logger) : IConnectorHost
{
    private readonly List<IExternalSystemConnector> _connectors = connectors.ToList();

    public async Task<IReadOnlyDictionary<string, string>> PublishAsync(
        ConnectorPublishRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.Project.Connector.Enabled ||
            string.IsNullOrWhiteSpace(request.Project.Connector.Provider) ||
            request.Project.Connector.Provider.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return DisabledMetadata();
        }

        var connector = _connectors.FirstOrDefault(candidate => candidate.CanHandle(request.Project));
        if (connector is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["connector"] = request.Project.Connector.Provider,
                ["status"] = "no_matching_connector",
                ["success"] = "false"
            };
        }

        try
        {
            var result = await connector.PublishAsync(request, cancellationToken);
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["connector"] = connector.Name,
                ["status"] = result.Status,
                ["success"] = result.Success ? "true" : "false"
            };

            if (!string.IsNullOrWhiteSpace(result.ExternalTransactionId))
            {
                metadata["externalTransactionId"] = result.ExternalTransactionId;
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                metadata["error"] = result.Error;
            }

            if (result.Metadata is not null)
            {
                foreach (var (key, value) in result.Metadata)
                {
                    metadata[key] = value;
                }
            }

            return metadata;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Connector publish failed. provider={Provider} source={SourcePath}",
                request.Project.Connector.Provider,
                request.File.File.SourcePath);

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["connector"] = connector.Name,
                ["status"] = "failed",
                ["success"] = "false",
                ["error"] = exception.Message
            };
        }
    }

    private static IReadOnlyDictionary<string, string> DisabledMetadata()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["connector"] = "none",
            ["status"] = "skipped",
            ["success"] = "true",
            ["reason"] = "Connector disabled for project."
        };
    }
}
