using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Interfaces;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class ProjectWiseStubConnector : IExternalSystemConnector
{
    public string Name => "projectwise_stub";

    public bool CanHandle(ProjectPolicy project)
    {
        if (!project.Connector.Enabled)
        {
            return false;
        }

        var provider = project.Connector.Provider.Trim();
        return provider.Equals("projectwise_stub", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("projectwise", StringComparison.OrdinalIgnoreCase);
    }

    public Task<ConnectorPublishResult> PublishAsync(ConnectorPublishRequest request, CancellationToken cancellationToken)
    {
        if (request.Project.Connector.Settings.TryGetValue("simulateFailure", out var simulateFailure) &&
            simulateFailure.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ProjectWise stub failure requested by policy setting.");
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["provider"] = "projectwise_stub",
            ["mode"] = "mvp_stub",
            ["projectId"] = request.Project.ProjectId,
            ["destinationPath"] = request.DestinationPath
        };

        var externalId = $"pwstub-{Guid.NewGuid():N}";
        return Task.FromResult(new ConnectorPublishResult(
            Success: true,
            Status: "published_stub",
            ExternalTransactionId: externalId,
            Metadata: metadata));
    }
}
