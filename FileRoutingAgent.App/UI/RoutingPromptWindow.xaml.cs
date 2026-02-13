using FileRoutingAgent.Core.Domain;
using System.IO;

namespace FileRoutingAgent.App.UI;

public partial class RoutingPromptWindow : System.Windows.Window
{
    private readonly PromptContext _context;

    public RoutingPromptWindow(PromptContext context)
    {
        _context = context;
        InitializeComponent();
        InitializeView();
    }

    public UserDecision? Decision { get; private set; }

    private void InitializeView()
    {
        var file = _context.ClassifiedFile.File;
        var projectId = _context.Project.ProjectId;

        if (_context.ClassifiedFile.Category == FileCategory.Pdf)
        {
            HeaderText.Text = "PDF saved outside official destination";
            BodyText.Text =
                $"This PDF was detected in a non-official folder.\nFile: {Path.GetFileName(file.SourcePath)}\nCurrent: {file.SourcePath}\nProject: {projectId}";

            CategoryPanel.Visibility = System.Windows.Visibility.Visible;
            foreach (var key in _context.PdfCategoryKeys)
            {
                CategoryCombo.Items.Add(key);
            }

            if (!string.IsNullOrWhiteSpace(_context.DefaultPdfCategory))
            {
                CategoryCombo.SelectedItem = _context.DefaultPdfCategory;
            }

            if (CategoryCombo.SelectedIndex < 0 && CategoryCombo.Items.Count > 0)
            {
                CategoryCombo.SelectedIndex = 0;
            }

            PublishCopyButton.Visibility = System.Windows.Visibility.Collapsed;
            MoveButton.Content = "Move (Recommended)";
        }
        else
        {
            HeaderText.Text = "CAD file updated in working folder";
            BodyText.Text =
                $"Publish this file to the official CAD location now?\nFile: {Path.GetFileName(file.SourcePath)}\nWorking: {file.SourcePath}\nPublish To: {_context.DestinationHint ?? "(configured route)"}";

            CategoryPanel.Visibility = System.Windows.Visibility.Collapsed;
            PublishCopyButton.Visibility = System.Windows.Visibility.Visible;
            PublishCopyButton.Content = "Publish Copy (Recommended)";
        }
    }

    private void MoveButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        Decision = new UserDecision(ProposedAction.Move, SelectedPdfCategory());
        DialogResult = true;
    }

    private void CopyButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        Decision = new UserDecision(ProposedAction.Copy, SelectedPdfCategory());
        DialogResult = true;
    }

    private void PublishCopyButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        Decision = new UserDecision(ProposedAction.PublishCopy, SelectedPdfCategory());
        DialogResult = true;
    }

    private void LeaveButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        Decision = new UserDecision(ProposedAction.Leave, SelectedPdfCategory(), IgnoreOnce: true);
        DialogResult = true;
    }

    private void SnoozeButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        Decision = new UserDecision(ProposedAction.None, SelectedPdfCategory(), Snooze: TimeSpan.FromHours(1));
        DialogResult = true;
    }

    private void IgnoreFolderButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        Decision = new UserDecision(ProposedAction.None, SelectedPdfCategory(), AlwaysIgnoreFolder: true);
        DialogResult = true;
    }

    private string? SelectedPdfCategory()
    {
        if (_context.ClassifiedFile.Category != FileCategory.Pdf)
        {
            return null;
        }

        return CategoryCombo.SelectedItem?.ToString() ?? _context.DefaultPdfCategory;
    }
}
