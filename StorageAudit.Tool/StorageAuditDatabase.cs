using Microsoft.Data.Sqlite;

namespace StorageAudit.Tool;

public sealed class StorageAuditDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private StorageAuditDatabase(SqliteConnection connection)
    {
        _connection = connection;
    }

    public static async Task<StorageAuditDatabase> CreateAsync(string databasePath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? throw new InvalidOperationException("Database folder could not be resolved."));
        var connection = new SqliteConnection($"Data Source={databasePath};Pooling=True");
        await connection.OpenAsync(cancellationToken);

        var database = new StorageAuditDatabase(connection);
        await database.InitializeAsync(cancellationToken);
        return database;
    }

    public WriteSession CreateWriteSession()
    {
        return new WriteSession(_connection);
    }

    public async Task FinalizeForReportingAsync(CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            """
            CREATE INDEX IF NOT EXISTS idx_file_inventory_size ON file_inventory(size_bytes DESC);
            CREATE INDEX IF NOT EXISTS idx_file_inventory_project_bucket ON file_inventory(project_bucket);
            CREATE INDEX IF NOT EXISTS idx_file_inventory_extension ON file_inventory(extension);
            CREATE INDEX IF NOT EXISTS idx_file_inventory_age ON file_inventory(age_days DESC);
            """,
            cancellationToken);
    }

    public async Task<IReadOnlyList<LargestFileRow>> GetLargestFilesAsync(int topFilesCount, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT project_bucket, full_path, parent_folder, extension, size_bytes, created_utc, last_write_utc, age_days
            FROM file_inventory
            ORDER BY size_bytes DESC, full_path ASC
            LIMIT $topFilesCount;
            """;
        command.Parameters.AddWithValue("$topFilesCount", topFilesCount);

        var rows = new List<LargestFileRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rank = 1;
        while (await reader.ReadAsync(cancellationToken))
        {
            var sizeBytes = reader.GetInt64(4);
            rows.Add(new LargestFileRow(
                rank++,
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                sizeBytes,
                BytesToGb(sizeBytes),
                reader.GetInt32(7),
                DateTimeOffset.Parse(reader.GetString(6)),
                DateTimeOffset.Parse(reader.GetString(5))));
        }

        return rows;
    }

    public async Task<IReadOnlyList<ProjectRollupRow>> GetProjectRollupsAsync(int archiveProjectAgeDays, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT
              project_bucket,
              COUNT(*) AS file_count,
              SUM(size_bytes) AS total_size_bytes,
              MIN(last_write_utc) AS oldest_write_utc,
              MAX(last_write_utc) AS newest_write_utc,
              SUM(CASE WHEN age_days >= 365 THEN size_bytes ELSE 0 END) AS bytes_older_than_1_year,
              SUM(CASE WHEN age_days >= 730 THEN size_bytes ELSE 0 END) AS bytes_older_than_2_years,
              SUM(CASE WHEN age_days >= 1825 THEN size_bytes ELSE 0 END) AS bytes_older_than_5_years
            FROM file_inventory
            GROUP BY project_bucket
            ORDER BY total_size_bytes DESC, project_bucket ASC;
            """;

        var rows = new List<ProjectRollupRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var totalSizeBytes = reader.GetInt64(2);
            var newestWriteUtc = DateTimeOffset.Parse(reader.GetString(4));
            rows.Add(new ProjectRollupRow(
                reader.GetString(0),
                reader.GetInt64(1),
                totalSizeBytes,
                BytesToGb(totalSizeBytes),
                DateTimeOffset.Parse(reader.GetString(3)),
                newestWriteUtc,
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                (DateTimeOffset.UtcNow - newestWriteUtc).TotalDays >= archiveProjectAgeDays));
        }

        return rows;
    }

    public async Task<IReadOnlyList<CandidateReviewRow>> GetCandidateReviewRowsAsync(long minSizeBytes, int minAgeDays, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT project_bucket, full_path, parent_folder, extension, size_bytes, age_days, last_write_utc
            FROM file_inventory
            WHERE size_bytes >= $minSizeBytes
              AND age_days >= $minAgeDays
            ORDER BY size_bytes DESC, age_days DESC, full_path ASC;
            """;
        command.Parameters.AddWithValue("$minSizeBytes", minSizeBytes);
        command.Parameters.AddWithValue("$minAgeDays", minAgeDays);

        var rows = new List<CandidateReviewRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sizeBytes = reader.GetInt64(4);
            var ageDays = reader.GetInt32(5);
            rows.Add(new CandidateReviewRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                sizeBytes,
                BytesToGb(sizeBytes),
                ageDays,
                DateTimeOffset.Parse(reader.GetString(6)),
                ClassifyDisposition(ageDays)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<ExtensionSummaryRow>> GetExtensionSummariesAsync(CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT
              CASE WHEN extension = '' THEN '[no extension]' ELSE extension END AS normalized_extension,
              COUNT(*) AS file_count,
              SUM(size_bytes) AS total_size_bytes,
              MIN(last_write_utc) AS oldest_write_utc,
              MAX(last_write_utc) AS newest_write_utc
            FROM file_inventory
            GROUP BY normalized_extension
            ORDER BY total_size_bytes DESC, normalized_extension ASC;
            """;

        var rows = new List<ExtensionSummaryRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var totalSizeBytes = reader.GetInt64(2);
            rows.Add(new ExtensionSummaryRow(
                reader.GetString(0),
                reader.GetInt64(1),
                totalSizeBytes,
                BytesToGb(totalSizeBytes),
                DateTimeOffset.Parse(reader.GetString(3)),
                DateTimeOffset.Parse(reader.GetString(4))));
        }

        return rows;
    }

    public async Task<IReadOnlyList<ScanIssueRow>> GetScanIssuesAsync(CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT path, error_type, message
            FROM scan_issues
            ORDER BY path ASC, error_type ASC;
            """;

        var rows = new List<ScanIssueRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ScanIssueRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return rows;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA temp_store=MEMORY;

            CREATE TABLE IF NOT EXISTS file_inventory (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              full_path TEXT NOT NULL,
              parent_folder TEXT NOT NULL,
              project_bucket TEXT NOT NULL,
              extension TEXT NOT NULL,
              size_bytes INTEGER NOT NULL,
              created_utc TEXT NOT NULL,
              last_write_utc TEXT NOT NULL,
              age_days INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS scan_issues (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              path TEXT NOT NULL,
              error_type TEXT NOT NULL,
              message TEXT NOT NULL
            );
            """,
            cancellationToken);
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static double BytesToGb(long sizeBytes)
    {
        return Math.Round(sizeBytes / 1024d / 1024d / 1024d, 3);
    }

    private static string ClassifyDisposition(int ageDays)
    {
        if (ageDays >= 365 * 5)
        {
            return "Review for Archive";
        }

        if (ageDays >= 365 * 2)
        {
            return "Review for Retention";
        }

        return "Needs Engineering Check";
    }

    public sealed class WriteSession : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private SqliteTransaction _transaction;
        private SqliteCommand _insertFileCommand;
        private SqliteCommand _insertIssueCommand;
        private int _pendingWrites;

        public WriteSession(SqliteConnection connection)
        {
            _connection = connection;
            _transaction = _connection.BeginTransaction();
            _insertFileCommand = CreateInsertFileCommand(_connection, _transaction);
            _insertIssueCommand = CreateInsertIssueCommand(_connection, _transaction);
        }

        public async Task InsertFileAsync(FileInventoryRow row, CancellationToken cancellationToken)
        {
            _insertFileCommand.Parameters["$fullPath"].Value = row.FullPath;
            _insertFileCommand.Parameters["$parentFolder"].Value = row.ParentFolder;
            _insertFileCommand.Parameters["$projectBucket"].Value = row.ProjectBucket;
            _insertFileCommand.Parameters["$extension"].Value = row.Extension;
            _insertFileCommand.Parameters["$sizeBytes"].Value = row.SizeBytes;
            _insertFileCommand.Parameters["$createdUtc"].Value = row.CreatedUtc.UtcDateTime.ToString("O");
            _insertFileCommand.Parameters["$lastWriteUtc"].Value = row.LastWriteUtc.UtcDateTime.ToString("O");
            _insertFileCommand.Parameters["$ageDays"].Value = row.AgeDays;
            await _insertFileCommand.ExecuteNonQueryAsync(cancellationToken);
            await FlushIfNeededAsync(cancellationToken);
        }

        public async Task InsertIssueAsync(ScanIssueRow row, CancellationToken cancellationToken)
        {
            _insertIssueCommand.Parameters["$path"].Value = row.Path;
            _insertIssueCommand.Parameters["$errorType"].Value = row.ErrorType;
            _insertIssueCommand.Parameters["$message"].Value = row.Message;
            await _insertIssueCommand.ExecuteNonQueryAsync(cancellationToken);
            await FlushIfNeededAsync(cancellationToken);
        }

        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            await _transaction.CommitAsync(cancellationToken);
            _pendingWrites = 0;
        }

        public async ValueTask DisposeAsync()
        {
            await _insertFileCommand.DisposeAsync();
            await _insertIssueCommand.DisposeAsync();
            await _transaction.DisposeAsync();
        }

        private async Task FlushIfNeededAsync(CancellationToken cancellationToken)
        {
            _pendingWrites++;
            if (_pendingWrites < 1000)
            {
                return;
            }

            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = _connection.BeginTransaction();

            await _insertFileCommand.DisposeAsync();
            await _insertIssueCommand.DisposeAsync();
            _insertFileCommand = CreateInsertFileCommand(_connection, _transaction);
            _insertIssueCommand = CreateInsertIssueCommand(_connection, _transaction);
            _pendingWrites = 0;
        }

        private static SqliteCommand CreateInsertFileCommand(SqliteConnection connection, SqliteTransaction transaction)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO file_inventory (
                  full_path,
                  parent_folder,
                  project_bucket,
                  extension,
                  size_bytes,
                  created_utc,
                  last_write_utc,
                  age_days
                ) VALUES (
                  $fullPath,
                  $parentFolder,
                  $projectBucket,
                  $extension,
                  $sizeBytes,
                  $createdUtc,
                  $lastWriteUtc,
                  $ageDays
                );
                """;
            command.Parameters.Add("$fullPath", SqliteType.Text);
            command.Parameters.Add("$parentFolder", SqliteType.Text);
            command.Parameters.Add("$projectBucket", SqliteType.Text);
            command.Parameters.Add("$extension", SqliteType.Text);
            command.Parameters.Add("$sizeBytes", SqliteType.Integer);
            command.Parameters.Add("$createdUtc", SqliteType.Text);
            command.Parameters.Add("$lastWriteUtc", SqliteType.Text);
            command.Parameters.Add("$ageDays", SqliteType.Integer);
            return command;
        }

        private static SqliteCommand CreateInsertIssueCommand(SqliteConnection connection, SqliteTransaction transaction)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO scan_issues (
                  path,
                  error_type,
                  message
                ) VALUES (
                  $path,
                  $errorType,
                  $message
                );
                """;
            command.Parameters.Add("$path", SqliteType.Text);
            command.Parameters.Add("$errorType", SqliteType.Text);
            command.Parameters.Add("$message", SqliteType.Text);
            return command;
        }
    }
}
