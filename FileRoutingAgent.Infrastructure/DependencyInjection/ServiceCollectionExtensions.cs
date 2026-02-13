using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Infrastructure.Configuration;
using FileRoutingAgent.Infrastructure.Hosting;
using FileRoutingAgent.Infrastructure.Pathing;
using FileRoutingAgent.Infrastructure.Persistence;
using FileRoutingAgent.Infrastructure.Pipeline;
using FileRoutingAgent.Infrastructure.Watching;
using Microsoft.Extensions.DependencyInjection;

namespace FileRoutingAgent.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFileRoutingInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<RuntimeConfigSnapshotAccessor>();

        services.AddSingleton<IPolicyIntegrityGuard, PolicyIntegrityGuard>();
        services.AddSingleton<IPolicyConfigManager, PolicyConfigManager>();
        services.AddSingleton<IPathCanonicalizer, PathCanonicalizer>();
        services.AddSingleton<IAuditStore, SqliteAuditStore>();
        services.AddSingleton<ISourceWatcher, SourceWatcher>();
        services.AddSingleton<IReconciliationScanner, ReconciliationScanner>();
        services.AddSingleton<IEventNormalizer, EventNormalizer>();
        services.AddSingleton<IAgentOriginSuppressor, AgentOriginSuppressor>();
        services.AddSingleton<IFileStabilityGate, FileStabilityGate>();
        services.AddSingleton<IFileClassifier, FileClassifier>();
        services.AddSingleton<IProjectRegistry, ProjectRegistry>();
        services.AddSingleton<IPromptOrchestrator, PromptOrchestrator>();
        services.AddSingleton<IRoutingRulesEngine, RoutingRulesEngine>();
        services.AddSingleton<IConflictResolver, ConflictResolver>();
        services.AddSingleton<ITransferEngine, TransferEngine>();
        services.AddSingleton<IRootAvailabilityTracker, RootAvailabilityTracker>();
        services.AddSingleton<IExternalSystemConnector, CommandProcessConnector>();
        services.AddSingleton<IExternalSystemConnector, ProjectWiseStubConnector>();
        services.AddSingleton<IConnectorHost, ConnectorHost>();
        services.AddSingleton<IScanScheduler, ScanScheduler>();

        services.AddSingleton<DetectionPipelineHostedService>();
        services.AddSingleton<IRuntimePolicyRefresher>(serviceProvider => serviceProvider.GetRequiredService<DetectionPipelineHostedService>());
        services.AddSingleton<IManualDetectionIngress>(serviceProvider => serviceProvider.GetRequiredService<DetectionPipelineHostedService>());
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<DetectionPipelineHostedService>());

        return services;
    }

    public static IServiceCollection ConfigureRuntimeOptions(
        this IServiceCollection services,
        Action<AgentRuntimeOptions>? configure = null)
    {
        services.AddOptions<AgentRuntimeOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services;
    }
}
