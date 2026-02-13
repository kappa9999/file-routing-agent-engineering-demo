using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Infrastructure.Pipeline;

namespace FileRoutingAgent.Tests;

public sealed class FileClassifierTests
{
    [Fact]
    public void Classify_IgnoresConfiguredTempPattern()
    {
        var policy = new FirmPolicy
        {
            ManagedExtensions = [".pdf"],
            IgnorePatterns = new IgnorePatternSettings
            {
                FileGlobs = ["*.tmp"],
                FolderGlobs = []
            }
        };
        var classifier = new FileClassifier();
        var stableFile = new StableFile(@"C:\tmp\draft.tmp", 1, DateTime.UtcNow, "x");

        var result = classifier.Classify(stableFile, policy);

        Assert.Null(result);
    }
}

