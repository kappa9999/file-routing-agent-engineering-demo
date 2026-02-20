using FileRoutingAgent.Core.Domain;
using System.Text.Json;

namespace FileRoutingAgent.App.UI;

public partial class DiagnosticsWindow : System.Windows.Window
{
    public DiagnosticsWindow(
        IReadOnlyCollection<RootStateSnapshot> rootStates,
        IReadOnlyList<ScanRunEntry> scanRuns,
        IReadOnlyList<AuditEventEntry> auditEvents,
        DemoModeState demoModeState,
        string? structureSummary)
    {
        InitializeComponent();

        var connectorRows = BuildConnectorRows(auditEvents);
        var mirrorRootCount = demoModeState.ProjectMirrorRoots.Count;
        var demoStatus = demoModeState.Enabled ? "ON (Mirror Only)" : "OFF";

        SummaryText.Text =
            $"Roots: {rootStates.Count} | Scan runs loaded: {scanRuns.Count} | Connector events: {connectorRows.Count}\n" +
            $"Demo Mode: {demoStatus} | Mirror roots: {mirrorRootCount} | Last mirror refresh: {(demoModeState.LastRefreshedUtc?.ToString("u") ?? "-")}\n" +
            $"Last structure summary: {(string.IsNullOrWhiteSpace(structureSummary) ? "-" : structureSummary)}";

        RootsGrid.ItemsSource = rootStates
            .OrderBy(snapshot => snapshot.RootPath)
            .Select(snapshot => new
            {
                snapshot.RootPath,
                State = snapshot.State.ToString(),
                UpdatedAtUtc = snapshot.UpdatedAtUtc.ToString("u"),
                snapshot.Note
            })
            .ToList();

        ScansGrid.ItemsSource = scanRuns
            .OrderByDescending(scan => scan.StartedUtc)
            .Select(scan => new
            {
                StartedUtc = scan.StartedUtc.ToString("u"),
                FinishedUtc = scan.FinishedUtc.ToString("u"),
                scan.RootPath,
                scan.CandidatesFound,
                scan.Queued,
                scan.Skipped,
                scan.Errors
            })
            .ToList();

        ConnectorGrid.ItemsSource = connectorRows;
    }

    private void CloseButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }

    private static List<object> BuildConnectorRows(IReadOnlyList<AuditEventEntry> auditEvents)
    {
        return auditEvents
            .Where(entry => entry.EventType.Equals("connector_publish", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.AtUtc)
            .Take(200)
            .Select(entry =>
            {
                var metadata = ParseMetadata(entry.PayloadJson);
                return (object)new
                {
                    AtUtc = entry.AtUtc.ToString("u"),
                    entry.ProjectId,
                    Connector = GetMetadataValue(metadata, "connector"),
                    Status = GetMetadataValue(metadata, "status"),
                    Success = GetMetadataValue(metadata, "success"),
                    ExternalTransactionId = GetMetadataValue(metadata, "externalTransactionId"),
                    entry.SourcePath,
                    DestinationPath = entry.DestinationPath,
                    Error = GetMetadataValue(metadata, "error")
                };
            })
            .ToList();
    }

    private static Dictionary<string, string> ParseMetadata(string? payloadJson)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return metadata;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return metadata;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                metadata[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => property.Value.GetRawText()
                };
            }
        }
        catch
        {
            metadata["status"] = "payload_parse_error";
        }

        return metadata;
    }

    private static string GetMetadataValue(IReadOnlyDictionary<string, string> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "-";
    }
}
