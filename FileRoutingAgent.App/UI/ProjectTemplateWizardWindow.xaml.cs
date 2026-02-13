using FileRoutingAgent.App.Services;
using FileRoutingAgent.Core.Configuration;
using Forms = System.Windows.Forms;

namespace FileRoutingAgent.App.UI;

public partial class ProjectTemplateWizardWindow : System.Windows.Window
{
    public ProjectTemplateWizardWindow()
    {
        InitializeComponent();
    }

    public ProjectPolicy? ProjectTemplate { get; private set; }

    private void BrowseButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select project root folder"
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            ProjectRootTextBox.Text = dialog.SelectedPath;
        }
    }

    private void AddButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var projectId = ProjectIdTextBox.Text.Trim();
        var displayName = DisplayNameTextBox.Text.Trim();
        var projectRoot = ProjectRootTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(projectRoot))
        {
            System.Windows.MessageBox.Show(
                "Project ID, Display Name, and Project Root are required.",
                "Invalid Template Input",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        ProjectTemplate = PolicyEditorUtility.BuildProjectTemplate(
            projectId,
            displayName,
            projectRoot,
            enableProjectWiseCommandProfile: EnableProjectWiseCommandProfileCheckBox.IsChecked == true);
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
