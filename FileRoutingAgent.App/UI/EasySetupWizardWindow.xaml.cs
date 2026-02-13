using System.IO;
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
        var folder = PickFolder("Select the project root folder");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        ProjectRootTextBox.Text = folder;
        ApplySuggestedPaths(folder);
    }

    private void AutoFillSuggestedFoldersButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var root = ProjectRootTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(root))
        {
            System.Windows.MessageBox.Show(
                "Select a Project Root folder first.",
                "Project Root Required",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        ApplySuggestedPaths(root);
    }

    private void BrowseWatchRootButton_OnClick(object sender, System.Windows.RoutedEventArgs e) => BrowseInto(WatchRootTextBox, "Select watch root folder");
    private void BrowseWorkingCadButton_OnClick(object sender, System.Windows.RoutedEventArgs e) => BrowseInto(WorkingCadTextBox, "Select working CAD folder");
    private void BrowseWorkingDesignButton_OnClick(object sender, System.Windows.RoutedEventArgs e) => BrowseInto(WorkingDesignTextBox, "Select working design folder");
    private void BrowseCadPublishButton_OnClick(object sender, System.Windows.RoutedEventArgs e) => BrowseInto(CadPublishTextBox, "Select official CAD publish folder");
    private void BrowsePlotSetsButton_OnClick(object sender, System.Windows.RoutedEventArgs e) => BrowseInto(PlotSetsTextBox, "Select official plot sets folder");
    private void BrowseProgressPrintsButton_OnClick(object sender, System.Windows.RoutedEventArgs e) => BrowseInto(ProgressPrintsTextBox, "Select progress prints folder");
    private void BrowseExhibitsButton_OnClick(object sender, System.Windows.RoutedEventArgs e) => BrowseInto(ExhibitsTextBox, "Select exhibits folder");
    private void BrowseCheckPrintsButton_OnClick(object sender, System.Windows.RoutedEventArgs e) => BrowseInto(CheckPrintsTextBox, "Select check prints folder");
    private void BrowseCleanSetsButton_OnClick(object sender, System.Windows.RoutedEventArgs e) => BrowseInto(CleanSetsTextBox, "Select clean sets folder");

    private void ApplySetupButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var projectId = ProjectIdTextBox.Text.Trim();
        var projectName = ProjectNameTextBox.Text.Trim();
        var projectRoot = NormalizePath(ProjectRootTextBox.Text);

        var watchRoot = NormalizePath(WatchRootTextBox.Text);
        var workingCad = NormalizePath(WorkingCadTextBox.Text);
        var workingDesign = NormalizePath(WorkingDesignTextBox.Text);
        var cadPublish = NormalizePath(CadPublishTextBox.Text);
        var plotSets = NormalizePath(PlotSetsTextBox.Text);
        var progressPrints = NormalizePath(ProgressPrintsTextBox.Text);
        var exhibits = NormalizePath(ExhibitsTextBox.Text);
        var checkPrints = NormalizePath(CheckPrintsTextBox.Text);
        var cleanSets = NormalizePath(CleanSetsTextBox.Text);

        if (string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(projectName) ||
            string.IsNullOrWhiteSpace(projectRoot) ||
            string.IsNullOrWhiteSpace(watchRoot) ||
            string.IsNullOrWhiteSpace(workingCad) ||
            string.IsNullOrWhiteSpace(workingDesign) ||
            string.IsNullOrWhiteSpace(cadPublish) ||
            string.IsNullOrWhiteSpace(plotSets) ||
            string.IsNullOrWhiteSpace(progressPrints) ||
            string.IsNullOrWhiteSpace(exhibits) ||
            string.IsNullOrWhiteSpace(checkPrints) ||
            string.IsNullOrWhiteSpace(cleanSets))
        {
            System.Windows.MessageBox.Show(
                "Please fill in all required folders before applying setup.",
                "Missing Required Fields",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        Result = new EasySetupInput(
            projectId,
            projectName,
            projectRoot,
            watchRoot,
            workingCad,
            workingDesign,
            cadPublish,
            plotSets,
            progressPrints,
            exhibits,
            checkPrints,
            cleanSets,
            IncludeLocalWrongSaveFoldersCheckBox.IsChecked == true,
            CreateStandardFoldersCheckBox.IsChecked == true,
            EnableProjectWiseCommandProfileCheckBox.IsChecked == true);

        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void BrowseInto(System.Windows.Controls.TextBox textBox, string description)
    {
        var folder = PickFolder(description);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            textBox.Text = folder;
        }
    }

    private void ApplySuggestedPaths(string projectRoot)
    {
        var root = NormalizePath(projectRoot);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        WatchRootTextBox.Text = root;
        WorkingCadTextBox.Text = Path.Combine(root, "60_CAD", "_Working");
        WorkingDesignTextBox.Text = Path.Combine(root, "70_Design", "_Working");
        CadPublishTextBox.Text = Path.Combine(root, "60_CAD", "Published");
        PlotSetsTextBox.Text = Path.Combine(root, "70_Design", "90_PlotSets");
        ProgressPrintsTextBox.Text = Path.Combine(root, "70_Design", "10_ProgressPrints");
        ExhibitsTextBox.Text = Path.Combine(root, "70_Design", "20_Exhibits");
        CheckPrintsTextBox.Text = Path.Combine(root, "70_Design", "30_CheckPrints");
        CleanSetsTextBox.Text = Path.Combine(root, "70_Design", "40_CleanSets");
    }

    private static string NormalizePath(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(trimmed).TrimEnd('\\');
        }
        catch
        {
            return trimmed.TrimEnd('\\');
        }
    }

    private static string? PickFolder(string description)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = description
        };

        return dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath)
            ? dialog.SelectedPath
            : null;
    }
}

public sealed record EasySetupInput(
    string ProjectId,
    string ProjectName,
    string ProjectRoot,
    string WatchRoot,
    string WorkingCadRoot,
    string WorkingDesignRoot,
    string CadPublishPath,
    string PlotSetsPath,
    string ProgressPrintsPath,
    string ExhibitsPath,
    string CheckPrintsPath,
    string CleanSetsPath,
    bool IncludeLocalWrongSaveFolders,
    bool CreateStandardFolders,
    bool EnableProjectWiseCommandProfile);
