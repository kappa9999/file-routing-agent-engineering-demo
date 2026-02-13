using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Infrastructure.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileRoutingAgent.Tests;

public sealed class ConnectorHostTests
{
    [Fact]
    public async Task PublishAsync_WhenConnectorDisabled_ReturnsSkippedMetadata()
    {
        var host = new ConnectorHost(Array.Empty<IExternalSystemConnector>(), NullLogger<ConnectorHost>.Instance);
        var request = CreateRequest(new ProjectPolicy
        {
            ProjectId = "Project123",
            DisplayName = "Project 123",
            Connector = new ExternalConnectorPolicy
            {
                Enabled = false,
                Provider = "none"
            }
        });

        var metadata = await host.PublishAsync(request, CancellationToken.None);

        Assert.Equal("skipped", metadata["status"]);
        Assert.Equal("true", metadata["success"]);
    }

    [Fact]
    public async Task PublishAsync_WhenNoConnectorMatches_ReturnsNoMatchingMetadata()
    {
        var host = new ConnectorHost(Array.Empty<IExternalSystemConnector>(), NullLogger<ConnectorHost>.Instance);
        var request = CreateRequest(new ProjectPolicy
        {
            ProjectId = "Project123",
            DisplayName = "Project 123",
            Connector = new ExternalConnectorPolicy
            {
                Enabled = true,
                Provider = "projectwise"
            }
        });

        var metadata = await host.PublishAsync(request, CancellationToken.None);

        Assert.Equal("no_matching_connector", metadata["status"]);
        Assert.Equal("false", metadata["success"]);
        Assert.Equal("projectwise", metadata["connector"]);
    }

    [Fact]
    public async Task PublishAsync_WhenConnectorThrows_ReturnsFailureMetadata()
    {
        var host = new ConnectorHost(
            [new ThrowingConnector()],
            NullLogger<ConnectorHost>.Instance);
        var request = CreateRequest(new ProjectPolicy
        {
            ProjectId = "Project123",
            DisplayName = "Project 123",
            Connector = new ExternalConnectorPolicy
            {
                Enabled = true,
                Provider = "projectwise_stub"
            }
        });

        var metadata = await host.PublishAsync(request, CancellationToken.None);

        Assert.Equal("failed", metadata["status"]);
        Assert.Equal("false", metadata["success"]);
        Assert.Equal("throwing_stub", metadata["connector"]);
    }

    private static ConnectorPublishRequest CreateRequest(ProjectPolicy project)
    {
        var sourcePath = @"C:\temp\sample.pdf";
        var file = new ClassifiedFile(
            new StableFile(sourcePath, 12, DateTime.UtcNow, "fingerprint"),
            FileCategory.Pdf,
            ".pdf");

        return new ConnectorPublishRequest(
            file,
            project,
            @"C:\official\sample.pdf",
            ProposedAction.Copy);
    }

    private sealed class ThrowingConnector : IExternalSystemConnector
    {
        public string Name => "throwing_stub";

        public bool CanHandle(ProjectPolicy project) => true;

        public Task<ConnectorPublishResult> PublishAsync(ConnectorPublishRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("boom");
        }
    }
}
