using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Infrastructure.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileRoutingAgent.Tests;

public sealed class CommandProcessConnectorTests
{
    [Fact]
    public async Task PublishAsync_WhenDotnetVersionCommandSucceeds_ReturnsCompleted()
    {
        var connector = new CommandProcessConnector(NullLogger<CommandProcessConnector>.Instance);
        var request = CreateRequest(new ExternalConnectorPolicy
        {
            Enabled = true,
            Provider = "command",
            Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["command"] = "dotnet",
                ["arguments"] = "--version",
                ["timeoutSeconds"] = "15"
            }
        });

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("completed", result.Status);
        Assert.NotNull(result.Metadata);
        Assert.Equal("0", result.Metadata!["exitCode"]);
        Assert.True(result.Metadata.ContainsKey("stdout"));
    }

    [Fact]
    public async Task PublishAsync_WhenCommandFails_ReturnsFailedStatus()
    {
        var connector = new CommandProcessConnector(NullLogger<CommandProcessConnector>.Instance);
        var request = CreateRequest(new ExternalConnectorPolicy
        {
            Enabled = true,
            Provider = "command",
            Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["command"] = "dotnet",
                ["arguments"] = "__not_a_valid_subcommand__",
                ["timeoutSeconds"] = "15"
            }
        });

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("failed", result.Status);
        Assert.NotNull(result.Error);
        Assert.NotNull(result.Metadata);
        Assert.True(result.Metadata!.ContainsKey("exitCode"));
    }

    [Fact]
    public void CanHandle_RecognizesCommandProviders()
    {
        var connector = new CommandProcessConnector(NullLogger<CommandProcessConnector>.Instance);
        var project = new ProjectPolicy
        {
            Connector = new ExternalConnectorPolicy
            {
                Enabled = true,
                Provider = "projectwise_script"
            }
        };

        Assert.True(connector.CanHandle(project));
    }

    private static ConnectorPublishRequest CreateRequest(ExternalConnectorPolicy connectorPolicy)
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), "connector-test.pdf");
        var file = new ClassifiedFile(
            new StableFile(sourcePath, 15, DateTime.UtcNow, "fp"),
            FileCategory.Pdf,
            ".pdf");

        var project = new ProjectPolicy
        {
            ProjectId = "Project123",
            DisplayName = "Project 123",
            Connector = connectorPolicy
        };

        return new ConnectorPublishRequest(
            file,
            project,
            Path.Combine(Path.GetTempPath(), "dest", "connector-test.pdf"),
            ProposedAction.Copy);
    }
}
