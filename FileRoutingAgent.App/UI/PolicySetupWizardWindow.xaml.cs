using FileRoutingAgent.App.Services;
using FileRoutingAgent.Core.Configuration;
using System.Collections.ObjectModel;
using Forms = System.Windows.Forms;

namespace FileRoutingAgent.App.UI;

public partial class PolicySetupWizardWindow : System.Windows.Window
{
    private readonly FirmPolicy _workingPolicy;
    private readonly Dictionary<string, string> _pdfCategories = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoading;
    private ProjectPolicy? _selectedProject;

    public PolicySetupWizardWindow(FirmPolicy policy)
    {
        _workingPolicy = PolicyEditorUtility.ClonePolicy(policy);
        InitializeComponent();
        Loaded += PolicySetupWizardWindow_OnLoaded;
    }

    public FirmPolicy? UpdatedPolicy { get; private set; }

    private void PolicySetupWizardWindow_OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _isLoading = true;
        try
        {
            PdfActionComboBox.ItemsSource = new ObservableCollection<string>(["move", "copy"]);
            CadActionComboBox.ItemsSource = new ObservableCollection<string>(["publish_copy", "copy", "move"]);
            OfficialDestinationModeComboBox.ItemsSource = new ObservableCollection<string>(["monitor_no_prompt", "prompt_enabled"]);
            ConnectorProviderComboBox.ItemsSource = new ObservableCollection<string>(["projectwise_script", "command", "projectwise_cli", "none"]);

            LoadGlobalSettings();
            RefreshProjectSelector(preferredProjectId: _workingPolicy.Projects.FirstOrDefault()?.ProjectId);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void LoadGlobalSettings()
    {
        ReconciliationIntervalTextBox.Text = _workingPolicy.Monitoring.ReconciliationIntervalMinutes.ToString();
        PromptCooldownTextBox.Text = _workingPolicy.Monitoring.PromptCooldownMinutes.ToString();
        RenameClusterTextBox.Text = _workingPolicy.Monitoring.RenameClusterWindowSeconds.ToString();

        ManagedExtensionsTextBox.Text = string.Join(", ", _workingPolicy.ManagedExtensions);
        IgnoreFileGlobsTextBox.Text = string.Join(Environment.NewLine, _workingPolicy.IgnorePatterns.FileGlobs);
        IgnoreFolderGlobsTextBox.Text = string.Join(Environment.NewLine, _workingPolicy.IgnorePatterns.FolderGlobs);

        SetListBoxItems(CandidateRootsListBox, _workingPolicy.Monitoring.CandidateRoots);
        SetListBoxItems(WatchRootsListBox, _workingPolicy.Monitoring.WatchRoots);

        StabilityMinAgeTextBox.Text = _workingPolicy.Stability.MinAgeSeconds.ToString();
        StabilityQuietTextBox.Text = _workingPolicy.Stability.QuietSeconds.ToString();
        StabilityChecksTextBox.Text = _workingPolicy.Stability.Checks.ToString();
        StabilityCheckIntervalTextBox.Text = _workingPolicy.Stability.CheckIntervalMs.ToString();
        RequireUnlockedCheckBox.IsChecked = _workingPolicy.Stability.RequireUnlocked;
        CopySafeOpenCheckBox.IsChecked = _workingPolicy.Stability.CopySafeOpen;

        SuppressionTtlTextBox.Text = _workingPolicy.Suppression.RecentOperationTtlMinutes.ToString();
        VersionSuffixTemplateTextBox.Text = _workingPolicy.ConflictPolicy.VersionSuffixTemplate;
        AllowOverwriteCheckBox.IsChecked = _workingPolicy.ConflictPolicy.AllowOverwriteWithConfirmation;
    }

    private void RefreshProjectSelector(string? preferredProjectId)
    {
        _isLoading = true;
        try
        {
            ProjectSelector.ItemsSource = null;
            ProjectSelector.ItemsSource = _workingPolicy.Projects;

            if (_workingPolicy.Projects.Count == 0)
            {
                _selectedProject = null;
                ClearProjectForm();
                return;
            }

            var target = !string.IsNullOrWhiteSpace(preferredProjectId)
                ? _workingPolicy.Projects.FirstOrDefault(project =>
                    project.ProjectId.Equals(preferredProjectId, StringComparison.OrdinalIgnoreCase))
                : null;

            target ??= _workingPolicy.Projects.First();
            ProjectSelector.SelectedItem = target;
            _selectedProject = target;
            LoadProjectSettings(target);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ProjectSelector_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        if (_selectedProject is not null)
        {
            SaveProjectSettings(_selectedProject);
        }

        _selectedProject = ProjectSelector.SelectedItem as ProjectPolicy;
        if (_selectedProject is null)
        {
            ClearProjectForm();
            return;
        }

        LoadProjectSettings(_selectedProject);
    }

    private void AddProjectButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_selectedProject is not null)
        {
            SaveProjectSettings(_selectedProject);
        }

        var wizard = new ProjectTemplateWizardWindow
        {
            Owner = this
        };

        if (wizard.ShowDialog() != true || wizard.ProjectTemplate is null)
        {
            return;
        }

        var existing = _workingPolicy.Projects.FirstOrDefault(project =>
            project.ProjectId.Equals(wizard.ProjectTemplate.ProjectId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var replace = System.Windows.MessageBox.Show(
                $"Project '{wizard.ProjectTemplate.ProjectId}' already exists. Replace it?",
                "Duplicate Project ID",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (replace != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            _workingPolicy.Projects.Remove(existing);
        }

        _workingPolicy.Projects.Add(wizard.ProjectTemplate);
        RefreshProjectSelector(wizard.ProjectTemplate.ProjectId);
    }

    private void RemoveProjectButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var selected = ProjectSelector.SelectedItem as ProjectPolicy;
        if (selected is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Remove project '{selected.ProjectId}' from this policy?",
            "Remove Project",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        _workingPolicy.Projects.Remove(selected);
        RefreshProjectSelector(preferredProjectId: _workingPolicy.Projects.FirstOrDefault()?.ProjectId);
    }

    private void LoadProjectSettings(ProjectPolicy project)
    {
        _isLoading = true;
        try
        {
            ProjectIdTextBox.Text = project.ProjectId;
            ProjectDisplayNameTextBox.Text = project.DisplayName;

            SetListBoxItems(PathMatchersListBox, project.PathMatchers);
            SetListBoxItems(WorkingRootsListBox, project.WorkingRoots);

            CadPublishTextBox.Text = project.OfficialDestinations.CadPublish;
            PlotSetsTextBox.Text = project.OfficialDestinations.PlotSets;

            _pdfCategories.Clear();
            foreach (var (key, path) in project.OfficialDestinations.PdfCategories)
            {
                _pdfCategories[key] = path;
            }

            RefreshPdfCategoriesList(project.Defaults.DefaultPdfCategory);

            PdfActionComboBox.SelectedItem = project.Defaults.PdfAction;
            CadActionComboBox.SelectedItem = project.Defaults.CadAction;
            OfficialDestinationModeComboBox.SelectedItem = project.Defaults.OfficialDestinationMode;

            ConnectorEnabledCheckBox.IsChecked = project.Connector.Enabled;
            ConnectorProviderComboBox.SelectedItem = string.IsNullOrWhiteSpace(project.Connector.Provider)
                ? "projectwise_script"
                : project.Connector.Provider;
            ConnectorCommandTextBox.Text = project.Connector.Settings.TryGetValue("command", out var command) ? command : string.Empty;
            ConnectorArgumentsTextBox.Text = project.Connector.Settings.TryGetValue("arguments", out var arguments) ? arguments : string.Empty;
            ConnectorTimeoutTextBox.Text = project.Connector.Settings.TryGetValue("timeoutSeconds", out var timeout) ? timeout : "120";
            ConnectorParseStdoutJsonCheckBox.IsChecked =
                project.Connector.Settings.TryGetValue("parseStdoutJson", out var parseStdout) &&
                parseStdout.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void SaveProjectSettings(ProjectPolicy project)
    {
        project.ProjectId = ProjectIdTextBox.Text.Trim();
        project.DisplayName = ProjectDisplayNameTextBox.Text.Trim();

        project.PathMatchers = GetListBoxItems(PathMatchersListBox);
        project.WorkingRoots = GetListBoxItems(WorkingRootsListBox);

        project.OfficialDestinations.CadPublish = CadPublishTextBox.Text.Trim();
        project.OfficialDestinations.PlotSets = PlotSetsTextBox.Text.Trim();
        project.OfficialDestinations.PdfCategories = new Dictionary<string, string>(_pdfCategories, StringComparer.OrdinalIgnoreCase);

        project.Defaults.PdfAction = (PdfActionComboBox.SelectedItem?.ToString() ?? "move").Trim();
        project.Defaults.CadAction = (CadActionComboBox.SelectedItem?.ToString() ?? "publish_copy").Trim();
        project.Defaults.OfficialDestinationMode = (OfficialDestinationModeComboBox.SelectedItem?.ToString() ?? "monitor_no_prompt").Trim();
        project.Defaults.DefaultPdfCategory = DefaultPdfCategoryComboBox.SelectedItem?.ToString()
                                              ?? _pdfCategories.Keys.FirstOrDefault()
                                              ?? "progress_print";

        var connector = project.Connector;
        connector.Enabled = ConnectorEnabledCheckBox.IsChecked == true;
        connector.Provider = (ConnectorProviderComboBox.SelectedItem?.ToString() ?? "projectwise_script").Trim();
        connector.Settings ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        SetOrRemoveSetting(connector.Settings, "command", ConnectorCommandTextBox.Text);
        SetOrRemoveSetting(connector.Settings, "arguments", ConnectorArgumentsTextBox.Text);
        SetOrRemoveSetting(connector.Settings, "timeoutSeconds", ConnectorTimeoutTextBox.Text);
        connector.Settings["parseStdoutJson"] = ConnectorParseStdoutJsonCheckBox.IsChecked == true ? "true" : "false";
    }

    private void ApplyButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            ApplyGlobalSettings();
            if (_selectedProject is not null)
            {
                SaveProjectSettings(_selectedProject);
            }

            var errors = PolicyEditorUtility.ValidatePolicy(_workingPolicy);
            if (errors.Count > 0)
            {
                System.Windows.MessageBox.Show(
                    "Policy validation failed:\n\n" + string.Join(Environment.NewLine, errors.Take(12)),
                    "Validation Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            UpdatedPolicy = _workingPolicy;
            DialogResult = true;
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Unable to apply settings:\n{exception.Message}",
                "Setup Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void CancelButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ApplyGlobalSettings()
    {
        _workingPolicy.ManagedExtensions = ParseExtensions(ManagedExtensionsTextBox.Text);
        _workingPolicy.Monitoring.CandidateRoots = GetListBoxItems(CandidateRootsListBox);
        _workingPolicy.Monitoring.WatchRoots = GetListBoxItems(WatchRootsListBox);
        _workingPolicy.IgnorePatterns.FileGlobs = ParseMultiline(IgnoreFileGlobsTextBox.Text);
        _workingPolicy.IgnorePatterns.FolderGlobs = ParseMultiline(IgnoreFolderGlobsTextBox.Text);

        _workingPolicy.Monitoring.ReconciliationIntervalMinutes = ParseInt(
            ReconciliationIntervalTextBox.Text,
            defaultValue: 5,
            min: 1,
            max: 1440,
            "Reconciliation interval");
        _workingPolicy.Monitoring.PromptCooldownMinutes = ParseInt(
            PromptCooldownTextBox.Text,
            defaultValue: 20,
            min: 1,
            max: 1440,
            "Prompt cooldown");
        _workingPolicy.Monitoring.RenameClusterWindowSeconds = ParseInt(
            RenameClusterTextBox.Text,
            defaultValue: 7,
            min: 1,
            max: 120,
            "Rename cluster window");

        _workingPolicy.Stability.MinAgeSeconds = ParseInt(StabilityMinAgeTextBox.Text, 3, 0, 120, "Stability min age");
        _workingPolicy.Stability.QuietSeconds = ParseInt(StabilityQuietTextBox.Text, 8, 0, 300, "Stability quiet window");
        _workingPolicy.Stability.Checks = ParseInt(StabilityChecksTextBox.Text, 3, 1, 20, "Stability checks");
        _workingPolicy.Stability.CheckIntervalMs = ParseInt(StabilityCheckIntervalTextBox.Text, 1500, 100, 10000, "Stability check interval");
        _workingPolicy.Stability.RequireUnlocked = RequireUnlockedCheckBox.IsChecked == true;
        _workingPolicy.Stability.CopySafeOpen = CopySafeOpenCheckBox.IsChecked == true;

        _workingPolicy.Suppression.RecentOperationTtlMinutes = ParseInt(
            SuppressionTtlTextBox.Text,
            defaultValue: 20,
            min: 1,
            max: 1440,
            "Suppression TTL");
        _workingPolicy.ConflictPolicy.VersionSuffixTemplate = VersionSuffixTemplateTextBox.Text.Trim();
        _workingPolicy.ConflictPolicy.AllowOverwriteWithConfirmation = AllowOverwriteCheckBox.IsChecked == true;
    }

    private void AddCandidateRootButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        AddFolderToList(CandidateRootsListBox, "Select candidate root (prompt-eligible source folder)");
    }

    private void RemoveCandidateRootButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        RemoveSelectedItem(CandidateRootsListBox);
    }

    private void AddWatchRootButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        AddFolderToList(WatchRootsListBox, "Select watch root (observed for hints/reconciliation)");
    }

    private void RemoveWatchRootButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        RemoveSelectedItem(WatchRootsListBox);
    }

    private void AddPathMatcherButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        AddFolderToList(PathMatchersListBox, "Select project path matcher");
    }

    private void RemovePathMatcherButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        RemoveSelectedItem(PathMatchersListBox);
    }

    private void AddWorkingRootButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        AddFolderToList(WorkingRootsListBox, "Select allowed working root");
    }

    private void RemoveWorkingRootButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        RemoveSelectedItem(WorkingRootsListBox);
    }

    private void BrowseCadPublishButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var folder = PickFolder("Select official CAD publish destination");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            CadPublishTextBox.Text = folder;
        }
    }

    private void BrowsePlotSetsButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var folder = PickFolder("Select official plot sets destination");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            PlotSetsTextBox.Text = folder;
        }
    }

    private void BrowsePdfCategoryPathButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var folder = PickFolder("Select PDF category destination");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            PdfCategoryPathTextBox.Text = folder;
        }
    }

    private void AddOrUpdatePdfCategoryButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var key = PdfCategoryKeyTextBox.Text.Trim();
        var path = PdfCategoryPathTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(path))
        {
            System.Windows.MessageBox.Show(
                "Both category key and destination path are required.",
                "Invalid PDF Category",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        _pdfCategories[key] = path;
        RefreshPdfCategoriesList(selectedKey: key);
    }

    private void RemovePdfCategoryButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var key = PdfCategoryKeyTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            var selected = PdfCategoriesListBox.SelectedItem?.ToString();
            key = ParsePdfCategoryKey(selected);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _pdfCategories.Remove(key);
        RefreshPdfCategoriesList(selectedKey: null);
    }

    private void PdfCategoriesListBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selected = PdfCategoriesListBox.SelectedItem?.ToString();
        var key = ParsePdfCategoryKey(selected);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        PdfCategoryKeyTextBox.Text = key;
        PdfCategoryPathTextBox.Text = _pdfCategories.TryGetValue(key, out var path) ? path : string.Empty;
    }

    private void UseSampleConnectorProfileButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var profile = PolicyEditorUtility.BuildProjectWiseCommandProfile(enabled: true);
        ConnectorEnabledCheckBox.IsChecked = profile.Enabled;
        ConnectorProviderComboBox.SelectedItem = profile.Provider;
        ConnectorCommandTextBox.Text = profile.Settings.TryGetValue("command", out var command) ? command : string.Empty;
        ConnectorArgumentsTextBox.Text = profile.Settings.TryGetValue("arguments", out var arguments) ? arguments : string.Empty;
        ConnectorTimeoutTextBox.Text = profile.Settings.TryGetValue("timeoutSeconds", out var timeout) ? timeout : "120";
        ConnectorParseStdoutJsonCheckBox.IsChecked =
            profile.Settings.TryGetValue("parseStdoutJson", out var parseStdout) &&
            parseStdout.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearProjectForm()
    {
        ProjectIdTextBox.Text = string.Empty;
        ProjectDisplayNameTextBox.Text = string.Empty;
        SetListBoxItems(PathMatchersListBox, Array.Empty<string>());
        SetListBoxItems(WorkingRootsListBox, Array.Empty<string>());
        CadPublishTextBox.Text = string.Empty;
        PlotSetsTextBox.Text = string.Empty;
        _pdfCategories.Clear();
        RefreshPdfCategoriesList(selectedKey: null);
        PdfActionComboBox.SelectedItem = "move";
        CadActionComboBox.SelectedItem = "publish_copy";
        OfficialDestinationModeComboBox.SelectedItem = "monitor_no_prompt";
        ConnectorEnabledCheckBox.IsChecked = false;
        ConnectorProviderComboBox.SelectedItem = "projectwise_script";
        ConnectorCommandTextBox.Text = string.Empty;
        ConnectorArgumentsTextBox.Text = string.Empty;
        ConnectorTimeoutTextBox.Text = "120";
        ConnectorParseStdoutJsonCheckBox.IsChecked = true;
    }

    private void RefreshPdfCategoriesList(string? selectedKey)
    {
        var items = _pdfCategories
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => $"{item.Key} => {item.Value}")
            .ToList();
        PdfCategoriesListBox.ItemsSource = items;

        var keys = _pdfCategories.Keys
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        DefaultPdfCategoryComboBox.ItemsSource = keys;

        if (!string.IsNullOrWhiteSpace(selectedKey) && keys.Contains(selectedKey, StringComparer.OrdinalIgnoreCase))
        {
            DefaultPdfCategoryComboBox.SelectedItem = keys.First(key => key.Equals(selectedKey, StringComparison.OrdinalIgnoreCase));
            return;
        }

        if (keys.Count > 0 && DefaultPdfCategoryComboBox.SelectedItem is null)
        {
            DefaultPdfCategoryComboBox.SelectedItem = keys[0];
        }
    }

    private static string ParsePdfCategoryKey(string? display)
    {
        if (string.IsNullOrWhiteSpace(display))
        {
            return string.Empty;
        }

        var separator = "=>";
        var index = display.IndexOf(separator, StringComparison.Ordinal);
        if (index <= 0)
        {
            return string.Empty;
        }

        return display[..index].Trim();
    }

    private static void SetListBoxItems(System.Windows.Controls.ListBox listBox, IEnumerable<string> values)
    {
        listBox.ItemsSource = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetListBoxItems(System.Windows.Controls.ListBox listBox)
    {
        return listBox.Items
            .Cast<object>()
            .Select(item => item.ToString()?.Trim() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ParseMultiline(string text)
    {
        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ParseExtensions(string raw)
    {
        var tokens = raw
            .Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => token.StartsWith('.') ? token : "." + token)
            .Select(token => token.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return tokens;
    }

    private static int ParseInt(string raw, int defaultValue, int min, int max, string label)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (!int.TryParse(raw.Trim(), out var parsed))
        {
            throw new InvalidOperationException($"{label} must be an integer.");
        }

        if (parsed < min || parsed > max)
        {
            throw new InvalidOperationException($"{label} must be between {min} and {max}.");
        }

        return parsed;
    }

    private static void SetOrRemoveSetting(IDictionary<string, string> settings, string key, string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            settings.Remove(key);
            return;
        }

        settings[key] = trimmed;
    }

    private static void AddFolderToList(System.Windows.Controls.ListBox listBox, string description)
    {
        var folder = PickFolder(description);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        var values = GetListBoxItems(listBox);
        if (!values.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(folder);
        }

        SetListBoxItems(listBox, values);
    }

    private static void RemoveSelectedItem(System.Windows.Controls.ListBox listBox)
    {
        if (listBox.SelectedItem is null)
        {
            return;
        }

        var selected = listBox.SelectedItem.ToString();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        var values = GetListBoxItems(listBox);
        values.RemoveAll(value => value.Equals(selected, StringComparison.OrdinalIgnoreCase));
        SetListBoxItems(listBox, values);
    }

    private static string? PickFolder(string description)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = description
        };

        return dialog.ShowDialog() == Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }
}
