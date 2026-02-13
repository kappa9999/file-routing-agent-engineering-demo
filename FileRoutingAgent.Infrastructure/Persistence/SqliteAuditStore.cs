using Dapper;
using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileRoutingAgent.Infrastructure.Persistence;

public sealed class SqliteAuditStore(
    IOptions<AgentRuntimeOptions> runtimeOptions,
    ILogger<SqliteAuditStore> logger) : IAuditStore
{
    private readonly string _dbPath = runtimeOptions.Value.DatabasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        EnsureParentDirectory(_dbPath);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync("PRAGMA journal_mode=WAL;");
        await connection.ExecuteAsync("PRAGMA busy_timeout=5000;");
        await connection.ExecuteAsync("PRAGMA synchronous=NORMAL;");

        var sql = """
                  CREATE TABLE IF NOT EXISTS state_watermarks (
                    root_path TEXT PRIMARY KEY,
                    last_scan_utc TEXT NOT NULL,
                    last_seen_path TEXT NULL
                  );

                  CREATE TABLE IF NOT EXISTS pending_items (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    source_path TEXT NOT NULL,
                    fingerprint TEXT NOT NULL,
                    project_id TEXT NULL,
                    category TEXT NOT NULL,
                    detected_at_utc TEXT NOT NULL,
                    source TEXT NOT NULL,
                    status TEXT NOT NULL,
                    last_error TEXT NULL
                  );

                  CREATE TABLE IF NOT EXISTS recent_operations (
                    dest_path TEXT NOT NULL,
                    size_bytes INTEGER NOT NULL,
                    last_write_utc TEXT NOT NULL,
                    recorded_at_utc TEXT NOT NULL,
                    PRIMARY KEY(dest_path, size_bytes, last_write_utc)
                  );

                  CREATE TABLE IF NOT EXISTS audit_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    at_utc TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    source_path TEXT NULL,
                    dest_path TEXT NULL,
                    fingerprint TEXT NULL,
                    project_id TEXT NULL,
                    payload_json TEXT NULL
                  );

                  CREATE TABLE IF NOT EXISTS scan_runs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_utc TEXT NOT NULL,
                    finished_utc TEXT NOT NULL,
                    root_path TEXT NOT NULL,
                    candidates_found INTEGER NOT NULL,
                    queued INTEGER NOT NULL,
                    skipped INTEGER NOT NULL,
                    errors INTEGER NOT NULL
                  );

                  CREATE TABLE IF NOT EXISTS user_ignores (
                    folder_path TEXT PRIMARY KEY,
                    created_utc TEXT NOT NULL
                  );

                  CREATE TABLE IF NOT EXISTS user_snoozes (
                    source_path TEXT PRIMARY KEY,
                    until_utc TEXT NOT NULL
                  );
                  """;

        await connection.ExecuteAsync(sql);
        logger.LogInformation("SQLite store initialized at {DbPath}", _dbPath);
    }

    public async Task WriteEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        await WithConnectionAsync(async connection =>
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO audit_events(at_utc, event_type, source_path, dest_path, fingerprint, project_id, payload_json)
                VALUES(@AtUtc, @EventType, @SourcePath, @DestinationPath, @Fingerprint, @ProjectId, @PayloadJson)
                """,
                new
                {
                    AtUtc = auditEvent.AtUtc.ToString("O"),
                    auditEvent.EventType,
                    auditEvent.SourcePath,
                    DestinationPath = auditEvent.DestinationPath,
                    auditEvent.Fingerprint,
                    auditEvent.ProjectId,
                    auditEvent.PayloadJson
                });
        }, cancellationToken);
    }

    public async Task RecordScanRunAsync(ScanRun scanRun, CancellationToken cancellationToken)
    {
        await WithConnectionAsync(async connection =>
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO scan_runs(started_utc, finished_utc, root_path, candidates_found, queued, skipped, errors)
                VALUES(@StartedUtc, @FinishedUtc, @RootPath, @CandidatesFound, @Queued, @Skipped, @Errors)
                """,
                new
                {
                    StartedUtc = scanRun.StartedUtc.ToString("O"),
                    FinishedUtc = scanRun.FinishedUtc.ToString("O"),
                    scanRun.RootPath,
                    scanRun.CandidatesFound,
                    scanRun.Queued,
                    scanRun.Skipped,
                    scanRun.Errors
                });
        }, cancellationToken);
    }

    public async Task SavePendingItemAsync(PendingItem pendingItem, CancellationToken cancellationToken)
    {
        await WithConnectionAsync(async connection =>
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO pending_items(source_path, fingerprint, project_id, category, detected_at_utc, source, status, last_error)
                SELECT @SourcePath, @Fingerprint, @ProjectId, @Category, @DetectedAtUtc, @Source, @Status, @LastError
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM pending_items
                    WHERE source_path = @SourcePath
                      AND fingerprint = @Fingerprint
                      AND status IN ('Pending', 'Processing')
                )
                """,
                new
                {
                    pendingItem.SourcePath,
                    pendingItem.Fingerprint,
                    pendingItem.ProjectId,
                    Category = pendingItem.Category.ToString(),
                    DetectedAtUtc = pendingItem.DetectedAtUtc.ToString("O"),
                    Source = pendingItem.Source.ToString(),
                    Status = pendingItem.Status.ToString(),
                    pendingItem.LastError
                });
        }, cancellationToken);
    }

    public async Task UpdatePendingStatusAsync(long id, PendingStatus status, string? lastError, CancellationToken cancellationToken)
    {
        await WithConnectionAsync(async connection =>
        {
            await connection.ExecuteAsync(
                """
                UPDATE pending_items
                SET status = @Status, last_error = @LastError
                WHERE id = @Id
                """,
                new
                {
                    Id = id,
                    Status = status.ToString(),
                    LastError = lastError
                });
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<PendingItem>> GetPendingItemsAsync(CancellationToken cancellationToken)
    {
        return await WithConnectionAsync(async connection =>
        {
            var rows = await connection.QueryAsync<PendingItemRow>(
                """
                SELECT
                  id AS Id,
                  source_path AS SourcePath,
                  fingerprint AS Fingerprint,
                  project_id AS ProjectId,
                  category AS Category,
                  detected_at_utc AS DetectedAtUtc,
                  source AS Source,
                  status AS Status,
                  last_error AS LastError
                FROM pending_items
                WHERE status IN ('Pending', 'Processing')
                ORDER BY detected_at_utc ASC
                """);

            return rows.Select(MapPending).ToList();
        }, cancellationToken);
    }

    public async Task SaveRecentOperationAsync(RecentOperation operation, CancellationToken cancellationToken)
    {
        await WithConnectionAsync(async connection =>
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO recent_operations(dest_path, size_bytes, last_write_utc, recorded_at_utc)
                VALUES(@DestinationPath, @SizeBytes, @LastWriteUtc, @RecordedAtUtc)
                ON CONFLICT(dest_path, size_bytes, last_write_utc)
                DO UPDATE SET recorded_at_utc = @RecordedAtUtc
                """,
                new
                {
                    DestinationPath = operation.DestinationPath,
                    operation.SizeBytes,
                    LastWriteUtc = operation.LastWriteUtc.ToString("O"),
                    RecordedAtUtc = operation.RecordedAtUtc.ToString("O")
                });
        }, cancellationToken);
    }

    public async Task<bool> IsRecentOperationAsync(
        string path,
        long sizeBytes,
        DateTime lastWriteUtc,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        return await WithConnectionAsync(async connection =>
        {
            var thresholdUtc = DateTime.UtcNow.Subtract(ttl).ToString("O");
            var count = await connection.ExecuteScalarAsync<long>(
                """
                SELECT COUNT(1)
                FROM recent_operations
                WHERE dest_path = @Path
                  AND size_bytes = @SizeBytes
                  AND last_write_utc = @LastWriteUtc
                  AND recorded_at_utc >= @ThresholdUtc
                """,
                new
                {
                    Path = path,
                    SizeBytes = sizeBytes,
                    LastWriteUtc = lastWriteUtc.ToString("O"),
                    ThresholdUtc = thresholdUtc
                });

            return count > 0;
        }, cancellationToken);
    }

    public async Task CleanupRecentOperationsAsync(DateTime olderThanUtc, CancellationToken cancellationToken)
    {
        await WithConnectionAsync(async connection =>
        {
            await connection.ExecuteAsync(
                "DELETE FROM recent_operations WHERE recorded_at_utc < @ThresholdUtc",
                new { ThresholdUtc = olderThanUtc.ToString("O") });
        }, cancellationToken);
    }

    public async Task<RootWatermark?> GetWatermarkAsync(string rootPath, CancellationToken cancellationToken)
    {
        return await WithConnectionAsync(async connection =>
        {
            var row = await connection.QueryFirstOrDefaultAsync<RootWatermarkRow>(
                """
                SELECT
                  root_path AS RootPath,
                  last_scan_utc AS LastScanUtc,
                  last_seen_path AS LastSeenPath
                FROM state_watermarks
                WHERE root_path = @RootPath
                """,
                new { RootPath = rootPath });

            if (row is null)
            {
                return null;
            }

            return new RootWatermark(
                row.RootPath,
                DateTime.Parse(row.LastScanUtc, null, System.Globalization.DateTimeStyles.RoundtripKind),
                row.LastSeenPath);
        }, cancellationToken);
    }

    public async Task SaveWatermarkAsync(RootWatermark watermark, CancellationToken cancellationToken)
    {
        await WithConnectionAsync(async connection =>
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO state_watermarks(root_path, last_scan_utc, last_seen_path)
                VALUES(@RootPath, @LastScanUtc, @LastSeenPath)
                ON CONFLICT(root_path)
                DO UPDATE SET last_scan_utc = @LastScanUtc, last_seen_path = @LastSeenPath
                """,
                new
                {
                    watermark.RootPath,
                    LastScanUtc = watermark.LastScanUtc.ToString("O"),
                    watermark.LastSeenPath
                });
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEventEntry>> GetRecentAuditEventsAsync(int limit, CancellationToken cancellationToken)
    {
        var cappedLimit = Math.Clamp(limit, 1, 500);
        return await WithConnectionAsync(async connection =>
        {
            var rows = await connection.QueryAsync<AuditEventRow>(
                """
                SELECT
                  id AS Id,
                  at_utc AS AtUtc,
                  event_type AS EventType,
                  source_path AS SourcePath,
                  dest_path AS DestinationPath,
                  fingerprint AS Fingerprint,
                  project_id AS ProjectId,
                  payload_json AS PayloadJson
                FROM audit_events
                ORDER BY id DESC
                LIMIT @Limit
                """,
                new { Limit = cappedLimit });

            return rows
                .Select(row => new AuditEventEntry(
                    row.Id,
                    DateTime.Parse(row.AtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind),
                    row.EventType,
                    row.SourcePath,
                    row.DestinationPath,
                    row.Fingerprint,
                    row.ProjectId,
                    row.PayloadJson))
                .ToList();
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ScanRunEntry>> GetRecentScanRunsAsync(int limit, CancellationToken cancellationToken)
    {
        var cappedLimit = Math.Clamp(limit, 1, 500);
        return await WithConnectionAsync(async connection =>
        {
            var rows = await connection.QueryAsync<ScanRunRow>(
                """
                SELECT
                  id AS Id,
                  started_utc AS StartedUtc,
                  finished_utc AS FinishedUtc,
                  root_path AS RootPath,
                  candidates_found AS CandidatesFound,
                  queued AS Queued,
                  skipped AS Skipped,
                  errors AS Errors
                FROM scan_runs
                ORDER BY id DESC
                LIMIT @Limit
                """,
                new { Limit = cappedLimit });

            return rows
                .Select(row => new ScanRunEntry(
                    row.Id,
                    DateTime.Parse(row.StartedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind),
                    DateTime.Parse(row.FinishedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind),
                    row.RootPath,
                    row.CandidatesFound,
                    row.Queued,
                    row.Skipped,
                    row.Errors))
                .ToList();
        }, cancellationToken);
    }

    private async Task WithConnectionAsync(Func<SqliteConnection, Task> action, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await action(connection);
    }

    private async Task<T> WithConnectionAsync<T>(Func<SqliteConnection, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await action(connection);
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_dbPath};Pooling=True");
    }

    private static PendingItem MapPending(PendingItemRow row)
    {
        _ = Enum.TryParse(row.Category, ignoreCase: true, out FileCategory category);
        _ = Enum.TryParse(row.Source, ignoreCase: true, out DetectionSource source);
        _ = Enum.TryParse(row.Status, ignoreCase: true, out PendingStatus status);
        return new PendingItem(
            row.Id,
            row.SourcePath,
            row.Fingerprint,
            row.ProjectId,
            category,
            DateTime.Parse(row.DetectedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind),
            source,
            status,
            row.LastError);
    }

    private static void EnsureParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parent))
        {
            return;
        }

        Directory.CreateDirectory(parent);
    }

    private sealed class PendingItemRow
    {
        public long Id { get; init; }
        public string SourcePath { get; init; } = string.Empty;
        public string Fingerprint { get; init; } = string.Empty;
        public string? ProjectId { get; init; }
        public string Category { get; init; } = string.Empty;
        public string DetectedAtUtc { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? LastError { get; init; }
    }

    private sealed class RootWatermarkRow
    {
        public string RootPath { get; init; } = string.Empty;
        public string LastScanUtc { get; init; } = string.Empty;
        public string? LastSeenPath { get; init; }
    }

    private sealed class AuditEventRow
    {
        public long Id { get; init; }
        public string AtUtc { get; init; } = string.Empty;
        public string EventType { get; init; } = string.Empty;
        public string? SourcePath { get; init; }
        public string? DestinationPath { get; init; }
        public string? Fingerprint { get; init; }
        public string? ProjectId { get; init; }
        public string? PayloadJson { get; init; }
    }

    private sealed class ScanRunRow
    {
        public long Id { get; init; }
        public string StartedUtc { get; init; } = string.Empty;
        public string FinishedUtc { get; init; } = string.Empty;
        public string RootPath { get; init; } = string.Empty;
        public int CandidatesFound { get; init; }
        public int Queued { get; init; }
        public int Skipped { get; init; }
        public int Errors { get; init; }
    }
}
