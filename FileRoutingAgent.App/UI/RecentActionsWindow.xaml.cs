using FileRoutingAgent.Core.Domain;

namespace FileRoutingAgent.App.UI;

public partial class RecentActionsWindow : System.Windows.Window
{
    public RecentActionsWindow(IReadOnlyList<AuditEventEntry> events)
    {
        InitializeComponent();
        SummaryText.Text = events.Count == 0
            ? "No recent actions."
            : $"Recent actions loaded: {events.Count}";

        EventsGrid.ItemsSource = events
            .Select(item => new
            {
                AtUtc = item.AtUtc.ToString("u"),
                item.EventType,
                item.ProjectId,
                item.SourcePath,
                item.DestinationPath
            })
            .ToList();
    }

    private void CloseButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }
}

