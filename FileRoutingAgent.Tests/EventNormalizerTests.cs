using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Infrastructure.Pipeline;

namespace FileRoutingAgent.Tests;

public sealed class EventNormalizerTests
{
    [Fact]
    public void TryNormalize_SuppressesDuplicateWithinCooldown()
    {
        var normalizer = new EventNormalizer();
        var cooldown = TimeSpan.FromMinutes(20);
        var candidate = new DetectionCandidate(@"C:\tmp\doc.pdf", DetectionSource.WatcherHint, DateTime.UtcNow);

        var first = normalizer.TryNormalize(candidate, cooldown, TimeSpan.FromSeconds(7), out _);
        var second = normalizer.TryNormalize(candidate, cooldown, TimeSpan.FromSeconds(7), out _);

        Assert.True(first);
        Assert.False(second);
    }
}
