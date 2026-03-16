using Microsoft.Data.Sqlite;

namespace StorageAudit.Tool;

public sealed class ProjectWiseReconcileDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private ProjectWiseReconcileDatabase(SqliteConnection connection)
    {
        _connection = connection;
    }

    public static async Task<ProjectWiseReconcileDatabase> CreateAsync(string databasePath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? throw new InvalidOperationException("Database folder could not be resolved."));
        var connection = new SqliteConnection($"Data Source={databasePath};Pooling=True");
        await connection.OpenAsync(cancellationToken);

        var database = new ProjectWiseReconcileDatabase(connection);
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
            CREATE INDEX IF NOT EXISTS idx_p_inventory_name_size ON p_drive_inventory(file_name_normalized, size_bytes);
            CREATE INDEX IF NOT EXISTS idx_p_inventory_last_write ON p_drive_inventory(last_write_unix DESC);
            CREATE INDEX IF NOT EXISTS idx_pw_inventory_name_size ON pw_inventory(file_name_normalized, size_bytes);
            CREATE INDEX IF NOT EXISTS idx_pw_inventory_last_write ON pw_inventory(last_write_unix DESC);
            """,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectWiseCompareRow>> GetAllCompareRowsAsync(
        DateTimeOffset cutoffUtc,
        TimeSpan tolerance,
        CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText =
            """
            WITH compare AS (
              SELECT
                p.full_path,
                p.parent_folder,
                p.file_name,
                p.extension,
                p.size_bytes,
                p.created_utc,
                p.last_write_utc,
                p.age_days,
                p.last_write_unix,
                (
                  SELECT COUNT(*)
                  FROM pw_inventory pw
                  WHERE pw.file_name_normalized = p.file_name_normalized
                    AND pw.size_bytes = p.size_bytes
                    AND ABS(pw.last_write_unix - p.last_write_unix) <= $toleranceSeconds
                ) AS exact_match_count,
                (
                  SELECT COUNT(*)
                  FROM pw_inventory pw
                  WHERE pw.file_name_normalized = p.file_name_normalized
                    AND pw.size_bytes = p.size_bytes
                ) AS same_name_size_count,
                COALESCE((
                  SELECT group_concat(pw.pw_path, ' | ')
                  FROM pw_inventory pw
                  WHERE pw.file_name_normalized = p.file_name_normalized
                    AND pw.size_bytes = p.size_bytes
                    AND ABS(pw.last_write_unix - p.last_write_unix) <= $toleranceSeconds
                ), '') AS exact_match_paths,
                COALESCE((
                  SELECT group_concat(pw.pw_path, ' | ')
                  FROM pw_inventory pw
                  WHERE pw.file_name_normalized = p.file_name_normalized
                    AND pw.size_bytes = p.size_bytes
                ), '') AS same_name_size_paths
              FROM p_drive_inventory p
            )
            SELECT
              full_path,
              parent_folder,
              file_name,
              extension,
              size_bytes,
              created_utc,
              last_write_utc,
              age_days,
              CASE
                WHEN exact_match_count = 1 THEN 'MatchedInPw'
                WHEN exact_match_count > 1 THEN 'AmbiguousNeedsReview'
                WHEN same_name_size_count > 0 THEN 'AmbiguousNeedsReview'
                ELSE 'MissingFromPw'
              END AS match_status,
              CASE WHEN last_write_unix >= $cutoffUnix THEN 1 ELSE 0 END AS changed_after_cutoff,
              CASE
                WHEN exact_match_count > 0 THEN exact_match_paths
                ELSE same_name_size_paths
              END AS matching_pw_paths,
              exact_match_count,
              same_name_size_count
            FROM compare
            ORDER BY full_path ASC;
            """;
        command.Parameters.AddWithValue("$toleranceSeconds", Convert.ToInt64(tolerance.TotalSeconds));
        command.Parameters.AddWithValue("$cutoffUnix", cutoffUtc.ToUnixTimeSeconds());

        var rows = new List<ProjectWiseCompareRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sizeBytes = reader.GetInt64(4);
            rows.Add(new ProjectWiseCompareRow(
                ParseStatus(reader.GetString(8)),
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                sizeBytes,
                BytesToGb(sizeBytes),
                DateTimeOffset.Parse(reader.GetString(5)),
                DateTimeOffset.Parse(reader.GetString(6)),
                reader.GetInt32(7),
                reader.GetInt64(9) == 1,
                reader.GetString(10),
                reader.GetInt32(11),
                reader.GetInt32(12)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<ProjectWiseImportIssueRow>> GetImportIssuesAsync(CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT row_number, source, error_type, message
            FROM import_issues
            ORDER BY COALESCE(row_number, 0) ASC, source ASC;
            """;

        var rows = new List<ProjectWiseImportIssueRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ProjectWiseImportIssueRow(
                reader.IsDBNull(0) ? null : reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
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

            CREATE TABLE IF NOT EXISTS p_drive_inventory (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              full_path TEXT NOT NULL,
              parent_folder TEXT NOT NULL,
              file_name TEXT NOT NULL,
              file_name_normalized TEXT NOT NULL,
              extension TEXT NOT NULL,
              size_bytes INTEGER NOT NULL,
              created_utc TEXT NOT NULL,
              last_write_utc TEXT NOT NULL,
              last_write_unix INTEGER NOT NULL,
              age_days INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS pw_inventory (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              pw_path TEXT NOT NULL,
              pw_folder TEXT NOT NULL,
              file_name TEXT NOT NULL,
              file_name_normalized TEXT NOT NULL,
              size_bytes INTEGER NOT NULL,
              last_write_utc TEXT NOT NULL,
              last_write_unix INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS import_issues (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              row_number INTEGER NULL,
              source TEXT NOT NULL,
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

    private static ProjectWiseMatchStatus ParseStatus(string status)
    {
        return status switch
        {
            "MatchedInPw" => ProjectWiseMatchStatus.MatchedInPw,
            "MissingFromPw" => ProjectWiseMatchStatus.MissingFromPw,
            _ => ProjectWiseMatchStatus.AmbiguousNeedsReview
        };
    }

    private static double BytesToGb(long sizeBytes)
    {
        return Math.Round(sizeBytes / 1024d / 1024d / 1024d, 3);
    }

    public sealed class WriteSession : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private SqliteTransaction _transaction;
        private SqliteCommand _insertPDriveCommand;
        private SqliteCommand _insertPwCommand;
        private SqliteCommand _insertIssueCommand;
        private int _pendingWrites;

        public WriteSession(SqliteConnection connection)
        {
            _connection = connection;
            _transaction = _connection.BeginTransaction();
            _insertPDriveCommand = CreateInsertPDriveCommand(_connection, _transaction);
            _insertPwCommand = CreateInsertPwCommand(_connection, _transaction);
            _insertIssueCommand = CreateInsertIssueCommand(_connection, _transaction);
        }

        public async Task InsertPDriveFileAsync(PDriveInventoryRow row, CancellationToken cancellationToken)
        {
            _insertPDriveCommand.Parameters["$fullPath"].Value = row.FullPath;
            _insertPDriveCommand.Parameters["$parentFolder"].Value = row.ParentFolder;
            _insertPDriveCommand.Parameters["$fileName"].Value = row.FileName;
            _insertPDriveCommand.Parameters["$fileNameNormalized"].Value = row.FileNameNormalized;
            _insertPDriveCommand.Parameters["$extension"].Value = row.Extension;
            _insertPDriveCommand.Parameters["$sizeBytes"].Value = row.SizeBytes;
            _insertPDriveCommand.Parameters["$createdUtc"].Value = row.CreatedUtc.UtcDateTime.ToString("O");
            _insertPDriveCommand.Parameters["$lastWriteUtc"].Value = row.LastWriteUtc.UtcDateTime.ToString("O");
            _insertPDriveCommand.Parameters["$lastWriteUnix"].Value = row.LastWriteUnix;
            _insertPDriveCommand.Parameters["$ageDays"].Value = row.AgeDays;
            await _insertPDriveCommand.ExecuteNonQueryAsync(cancellationToken);
            await FlushIfNeededAsync(cancellationToken);
        }

        public async Task InsertPwInventoryRowAsync(ProjectWiseInventoryRow row, CancellationToken cancellationToken)
        {
            _insertPwCommand.Parameters["$pwPath"].Value = row.PwPath;
            _insertPwCommand.Parameters["$pwFolder"].Value = row.PwFolder;
            _insertPwCommand.Parameters["$fileName"].Value = row.FileName;
            _insertPwCommand.Parameters["$fileNameNormalized"].Value = row.FileNameNormalized;
            _insertPwCommand.Parameters["$sizeBytes"].Value = row.SizeBytes;
            _insertPwCommand.Parameters["$lastWriteUtc"].Value = row.LastWriteUtc.UtcDateTime.ToString("O");
            _insertPwCommand.Parameters["$lastWriteUnix"].Value = row.LastWriteUnix;
            await _insertPwCommand.ExecuteNonQueryAsync(cancellationToken);
            await FlushIfNeededAsync(cancellationToken);
        }

        public async Task InsertImportIssueAsync(ProjectWiseImportIssueRow row, CancellationToken cancellationToken)
        {
            _insertIssueCommand.Parameters["$rowNumber"].Value = row.RowNumber is null ? DBNull.Value : row.RowNumber.Value;
            _insertIssueCommand.Parameters["$source"].Value = row.Source;
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
            await _insertPDriveCommand.DisposeAsync();
            await _insertPwCommand.DisposeAsync();
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

            await _insertPDriveCommand.DisposeAsync();
            await _insertPwCommand.DisposeAsync();
            await _insertIssueCommand.DisposeAsync();
            _insertPDriveCommand = CreateInsertPDriveCommand(_connection, _transaction);
            _insertPwCommand = CreateInsertPwCommand(_connection, _transaction);
            _insertIssueCommand = CreateInsertIssueCommand(_connection, _transaction);
            _pendingWrites = 0;
        }

        private static SqliteCommand CreateInsertPDriveCommand(SqliteConnection connection, SqliteTransaction transaction)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO p_drive_inventory (
                  full_path,
                  parent_folder,
                  file_name,
                  file_name_normalized,
                  extension,
                  size_bytes,
                  created_utc,
                  last_write_utc,
                  last_write_unix,
                  age_days
                ) VALUES (
                  $fullPath,
                  $parentFolder,
                  $fileName,
                  $fileNameNormalized,
                  $extension,
                  $sizeBytes,
                  $createdUtc,
                  $lastWriteUtc,
                  $lastWriteUnix,
                  $ageDays
                );
                """;
            command.Parameters.Add("$fullPath", SqliteType.Text);
            command.Parameters.Add("$parentFolder", SqliteType.Text);
            command.Parameters.Add("$fileName", SqliteType.Text);
            command.Parameters.Add("$fileNameNormalized", SqliteType.Text);
            command.Parameters.Add("$extension", SqliteType.Text);
            command.Parameters.Add("$sizeBytes", SqliteType.Integer);
            command.Parameters.Add("$createdUtc", SqliteType.Text);
            command.Parameters.Add("$lastWriteUtc", SqliteType.Text);
            command.Parameters.Add("$lastWriteUnix", SqliteType.Integer);
            command.Parameters.Add("$ageDays", SqliteType.Integer);
            return command;
        }

        private static SqliteCommand CreateInsertPwCommand(SqliteConnection connection, SqliteTransaction transaction)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO pw_inventory (
                  pw_path,
                  pw_folder,
                  file_name,
                  file_name_normalized,
                  size_bytes,
                  last_write_utc,
                  last_write_unix
                ) VALUES (
                  $pwPath,
                  $pwFolder,
                  $fileName,
                  $fileNameNormalized,
                  $sizeBytes,
                  $lastWriteUtc,
                  $lastWriteUnix
                );
                """;
            command.Parameters.Add("$pwPath", SqliteType.Text);
            command.Parameters.Add("$pwFolder", SqliteType.Text);
            command.Parameters.Add("$fileName", SqliteType.Text);
            command.Parameters.Add("$fileNameNormalized", SqliteType.Text);
            command.Parameters.Add("$sizeBytes", SqliteType.Integer);
            command.Parameters.Add("$lastWriteUtc", SqliteType.Text);
            command.Parameters.Add("$lastWriteUnix", SqliteType.Integer);
            return command;
        }

        private static SqliteCommand CreateInsertIssueCommand(SqliteConnection connection, SqliteTransaction transaction)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO import_issues (
                  row_number,
                  source,
                  error_type,
                  message
                ) VALUES (
                  $rowNumber,
                  $source,
                  $errorType,
                  $message
                );
                """;
            command.Parameters.Add("$rowNumber", SqliteType.Integer);
            command.Parameters.Add("$source", SqliteType.Text);
            command.Parameters.Add("$errorType", SqliteType.Text);
            command.Parameters.Add("$message", SqliteType.Text);
            return command;
        }
    }
}
