using Forms = System.Windows.Forms;

namespace FileRoutingAgent.App.UI;

public partial class EasySetupWizardWindow : System.Windows.Window
{
    public EasySetupWizardWindow()
    {
        InitializeComponent();
        ProjectIdTextBox.Text = "Project123";
        ProjectNameTextBox.Text = "Project 123";
    }

    public EasySetupInput? Result { get; private set; }

    private void BrowseProjectRootButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select the project root folder"
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            ProjectRootTextBox.Text = dialog.SelectedPath;
        }
    }

    private void ApplySetupButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var projectId = ProjectIdTextBox.Text.Trim();
        var projectName = ProjectNameTextBox.Text.Trim();
        var projectRoot = ProjectRootTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(projectName) ||
            string.IsNullOrWhiteSpace(projectRoot))
        {
            System.Windows.MessageBox.Show(
                "Project ID, Project Name, and Project Root are required.",
                "Missing Required Fields",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        Result = new EasySetupInput(
            projectId,
            projectName,
            projectRoot,
            IncludeLocalWrongSaveFoldersCheckBox.IsChecked == true,
            CreateStandardFoldersCheckBox.IsChecked == true,
            EnableProjectWiseCommandProfileCheckBox.IsChecked == true);

        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

public sealed record EasySetupInput(
    string ProjectId,
    string ProjectName,
    string ProjectRoot,
    bool IncludeLocalWrongSaveFolders,
    bool CreateStandardFolders,
    bool EnableProjectWiseCommandProfile);
