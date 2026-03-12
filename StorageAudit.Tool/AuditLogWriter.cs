namespace StorageAudit.Tool;

public sealed class AuditLogWriter : IDisposable
{
    private readonly object _sync = new();
    private readonly StreamWriter _writer;

    public AuditLogWriter(string logPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? throw new InvalidOperationException("Log folder could not be resolved."));
        _writer = new StreamWriter(File.Open(logPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    public void Error(string message, Exception exception)
    {
        Write("ERROR", $"{message} | {exception.GetType().Name}: {exception.Message}");
    }

    private void Write(string level, string message)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        lock (_sync)
        {
            Console.WriteLine(line);
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}
