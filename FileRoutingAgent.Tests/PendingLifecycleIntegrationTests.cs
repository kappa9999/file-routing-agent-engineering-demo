using Dapper;
using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Infrastructure.Configuration;
using FileRoutingAgent.Infrastructure.Hosting;
using FileRoutingAgent.Infrastructure.Pathing;
using FileRoutingAgent.Infrastructure.Persistence;
using FileRoutingAgent.Infrastructure.Pipeline;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FileRoutingAgent.Tests;

public sealed class PendingLifecycleIntegrationTests
{
    [Fact]
    public async Task RestoredProcessingItem_TransfersAndTransitionsToDone()
    {
        var root = Path.Combine(Path.GetTempPath(), "FileRoutingAgentTests", Guid.NewGuid().ToString("N"));
        var workingRoot = Path.Combine(root, "Project123", "70_Design", "_Working");
        var progressRoot = Path.Combine(root, "Project123", "70_Design", "10_ProgressPrints");
        Directory.CreateDirectory(workingRoot);
        Directory.CreateDirectory(progressRoot);

        var sourcePath = Path.Combine(workingRoot, "pending-smoke.pdf");
        await File.WriteAllTextAsync(sourcePath, "pending lifecycle smoke");

        var dbPath = Path.Combine(root, "state.db");
        var options = Options.Create(new AgentRuntimeOptions { DatabasePath = dbPath });
        var auditStore = new SqliteAuditStore(options, NullLogger<SqliteAuditStore>.Instance);
        await auditStore.InitializeAsync(CancellationToken.None);

        await auditStore.SavePendingItemAsync(
            new PendingItem(
                0,
                sourcePath,
                "fingerprint-seed",
                "Project123",
                FileCategory.Pdf,
                DateTime.UtcNow,
                DetectionSource.ReconciliationScan,
                PendingStatus.Processing,
                "Seeded by test."),
            CancellationToken.None);

        var seededPending = await auditStore.GetPendingItemsAsync(CancellationToken.None);
        var pendingId = seededPending.Single().Id;

        var policy = BuildPolicy(root);
        var snapshot = new RuntimeConfigSnapshot
        {
            PolicyPath = Path.Combine(root, "firm-policy.json"),
            UserPreferencesPath = Path.Combine(root, "user-preferences.json"),
            Policy = policy,
            UserPreferences = new UserPreferences(),
            SafeModeEnabled = false
        };

        await File.WriteAllTextAsync(snapshot.PolicyPath, "{}");
        var configManager = new TestPolicyConfigManager(snapshot);
        var accessor = new RuntimeConfigSnapshotAccessor();
        var pathCanonicalizer = new PathCanonicalizer();
        var rootAvailabilityTracker = new RootAvailabilityTracker();

        var service = new DetectionPipelineHostedService(
            configManager,
            accessor,
            auditStore,
            new NoopSourceWatcher(),
            new NoopScanner(),
            new EventNormalizer(),
            new AgentOriginSuppressor(auditStore, accessor),
            new FileStabilityGate(NullLogger<FileStabilityGate>.Instance),
            new FileClassifier(),
            new ProjectRegistry(pathCanonicalizer),
            new FixedPromptOrchestrator(new UserDecision(ProposedAction.Move, "progress_print")),
            new RoutingRulesEngine(),
            new ConflictResolver(new FixedUserPromptService(), accessor, pathCanonicalizer, NullLogger<ConflictResolver>.Instance),
            new TransferEngine(rootAvailabilityTracker, NullLogger<TransferEngine>.Instance),
            pathCanonicalizer,
            new ConnectorHost(Array.Empty<IExternalSystemConnector>(), NullLogger<ConnectorHost>.Instance),
            new ScanScheduler(),
            new DemoSnapshotTransformer(),
            new DemoSafetyGuard(pathCanonicalizer),
            NullLogger<DetectionPipelineHostedService>.Instance);

        try
        {
            await service.StartAsync(CancellationToken.None);

            var destinationPath = Path.Combine(progressRoot, Path.GetFileName(sourcePath));
            var moved = await WaitForAsync(
                () => File.Exists(destinationPath) && !File.Exists(sourcePath),
                TimeSpan.FromSeconds(20));

            Assert.True(moved, "Pending file was not transferred to destination in time.");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        await using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=True");
        await connection.OpenAsync();

        var status = await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM pending_items WHERE id = @Id",
            new { Id = pendingId });
        Assert.Equal(PendingStatus.Done.ToString(), status);

        var transferEvents = await connection.QueryAsync<string>(
            "SELECT event_type FROM audit_events WHERE event_type IN ('transfer_success', 'transfer_failure')");
        Assert.Contains("transfer_success", transferEvents);

        await CleanupDirectoryAsync(root);
    }

    private static FirmPolicy BuildPolicy(string root)
    {
        var projectRoot = Path.Combine(root, "Project123");
        return new FirmPolicy
        {
            SchemaVersion = 2,
            Monitoring = new MonitoringSettings
            {
                CandidateRoots = [Path.Combine(projectRoot, "70_Design", "_Working")],
                WatchRoots = [projectRoot],
                ReconciliationIntervalMinutes = 60,
                PromptCooldownMinutes = 1,
                RenameClusterWindowSeconds = 1
            },
            ManagedExtensions = [".pdf"],
            IgnorePatterns = new IgnorePatternSettings(),
            Suppression = new SuppressionSettings
            {
                RecentOperationTtlMinutes = 20
            },
            Stability = new StabilitySettings
            {
                MinAgeSeconds = 0,
                QuietSeconds = 1,
                Checks = 1,
                CheckIntervalMs = 100,
                RequireUnlocked = false,
                CopySafeOpen = false
            },
            Projects =
            [
                new ProjectPolicy
                {
                    ProjectId = "Project123",
                    DisplayName = "Project 123",
                    PathMatchers = [projectRoot],
                    WorkingRoots = [Path.Combine(projectRoot, "70_Design", "_Working")],
                    OfficialDestinations = new OfficialDestinations
                    {
                        CadPublish = Path.Combine(projectRoot, "60_CAD", "Published"),
                        PlotSets = Path.Combine(projectRoot, "70_Design", "90_PlotSets"),
                        PdfCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["progress_print"] = Path.Combine(projectRoot, "70_Design", "10_ProgressPrints")
                        }
                    },
                    Defaults = new ProjectDefaults
                    {
                        PdfAction = "move",
                        CadAction = "publish_copy",
                        DefaultPdfCategory = "progress_print",
                        OfficialDestinationMode = "monitor_no_prompt"
                    }
                }
            ]
        };
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var startedUtc = DateTime.UtcNow;
        while (DateTime.UtcNow - startedUtc < timeout)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(150);
        }

        return condition();
    }

    private static async Task CleanupDirectoryAsync(string root)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            try
            {
                Directory.Delete(root, true);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(120);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(120);
            }
        }
    }

    private sealed class TestPolicyConfigManager(RuntimeConfigSnapshot snapshot) : IPolicyConfigManager
    {
        private RuntimeConfigSnapshot _snapshot = snapshot;

        public Task<RuntimeConfigSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_snapshot);
        }

        public Task SaveUserPreferencesAsync(UserPreferences preferences, CancellationToken cancellationToken)
        {
            _snapshot = new RuntimeConfigSnapshot
            {
                PolicyPath = _snapshot.PolicyPath,
                UserPreferencesPath = _snapshot.UserPreferencesPath,
                Policy = _snapshot.Policy,
                UserPreferences = preferences,
                SafeModeEnabled = _snapshot.SafeModeEnabled,
                SafeModeReason = _snapshot.SafeModeReason
            };
            return Task.CompletedTask;
        }
    }

    private sealed class NoopSourceWatcher : ISourceWatcher
    {
        public Task StartAsync(
            Func<DetectionCandidate, ValueTask> onCandidate,
            Func<string, ValueTask>? onOverflow,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopScanner : IReconciliationScanner
    {
        public Task<ScanRun> RunScanAsync(
            Func<DetectionCandidate, ValueTask> onCandidate,
            bool priority,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ScanRun(
                DateTime.UtcNow,
                DateTime.UtcNow,
                "test",
                0,
                0,
                0,
                0));
        }
    }

    private sealed class FixedPromptOrchestrator(UserDecision decision) : IPromptOrchestrator
    {
        public Task<UserDecision> RequestDecisionAsync(PromptContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(decision);
        }
    }

    private sealed class FixedUserPromptService : IUserPromptService
    {
        public Task<UserDecision> PromptForRoutingAsync(PromptContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(new UserDecision(ProposedAction.Move, context.DefaultPdfCategory));
        }

        public Task<ConflictChoice> PromptForConflictAsync(TransferPlan transferPlan, CancellationToken cancellationToken)
        {
            return Task.FromResult(ConflictChoice.KeepBothVersioned);
        }

        public Task<ConflictChoice> PromptForInvalidDestinationAsync(
            IReadOnlyList<ConflictValidationError> validationErrors,
            string sourcePath,
            string suggestedPath,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ConflictChoice.KeepBothVersioned);
        }
    }
}
