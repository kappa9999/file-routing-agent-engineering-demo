using System.Text;

namespace StorageAudit.Tool;

public static class StorageAuditCsvWriter
{
    public static async Task WriteAsync<T>(
        string path,
        IReadOnlyList<string> headers,
        IReadOnlyList<T> rows,
        Func<T, IReadOnlyList<object?>> valueSelector,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("CSV folder could not be resolved."));

        await using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await writer.WriteLineAsync(string.Join(",", headers.Select(Escape)));
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = valueSelector(row).Select(FormatValue).Select(Escape);
            await writer.WriteLineAsync(string.Join(",", values));
        }
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("u"),
            DateTime dateTime => dateTime.ToUniversalTime().ToString("u"),
            double doubleValue => doubleValue.ToString("0.###"),
            float floatValue => floatValue.ToString("0.###"),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
