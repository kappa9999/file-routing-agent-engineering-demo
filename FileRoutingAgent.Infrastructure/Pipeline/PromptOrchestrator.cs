using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class PromptOrchestrator(
    IUserPromptService userPromptService,
    ILogger<PromptOrchestrator> logger) : IPromptOrchestrator
{
    public async Task<UserDecision> RequestDecisionAsync(PromptContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await userPromptService.PromptForRoutingAsync(context, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Prompt failed for {Path}. Falling back to safe action.", context.ClassifiedFile.File.SourcePath);
            return new UserDecision(ProposedAction.Leave, IgnoreOnce: true);
        }
    }
}

