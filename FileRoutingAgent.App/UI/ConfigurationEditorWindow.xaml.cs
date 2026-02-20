using FileRoutingAgent.App.Services;
using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Infrastructure.Configuration;
using FileRoutingAgent.Infrastructure.Pipeline;
using System.IO;

namespace FileRoutingAgent.App.UI;

public partial class ConfigurationEditorWindow : System.Windows.Window
{
    private readonly RuntimeConfigSnapshotAccessor _snapshotAccessor;
    private readonly IRuntimePolicyRefresher _runtimePolicyRefresher;
    private readonly IPolicyConfigManager _policyConfigManager;
    private readonly IProjectStructureAuditor _projectStructureAuditor;
    private readonly IDemoMirrorService _demoMirrorService;
    private readonly IPathCanonicalizer _pathCanonicalizer;

    private string _policyPath = string.Empty;
    private string _signaturePath = string.Empty;

    public ConfigurationEditorWindow(
        RuntimeConfigSnapshotAccessor snapshotAccessor,
        IRuntimePolicyRefresher runtimePolicyRefresher,
        IPolicyConfigManager policyConfigManager,
        IProjectStructureAuditor projectStructureAuditor,
        IDemoMirrorService demoMirrorService,
        IPathCanonicalizer pathCanonicalizer)
    {
        _snapshotAccessor = snapshotAccessor;
        _runtimePolicyRefresher = runtimePolicyRefresher;
        _policyConfigManager = policyConfigManager;
        _projectStructureAuditor = projectStructureAuditor;
        _demoMirrorService = demoMirrorService;
        _pathCanonicalizer = pathCanonicalizer;

        InitializeComponent();
        Loaded += ConfigurationEditorWindow_OnLoaded;
    }

    private async void ConfigurationEditorWindow_OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        await LoadFromDiskAsync();
    }

    private async void ReloadButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        await LoadFromDiskAsync();
    }

    private void ValidateButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        ValidateCurrentPolicy(showSuccess: true);
    }

    private void GuidedSetupButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!TryGetPolicy(out var policy))
        {
            return;
        }

        var wizard = new PolicySetupWizardWindow(policy!)
        {
            Owner = this
        };

        if (wizard.ShowDialog() != true || wizard.UpdatedPolicy is null)
        {
            return;
        }

        PolicyJsonTextBox.Text = PolicyEditorUtility.SerializePolicy(wizard.UpdatedPolicy);
        ValidateCurrentPolicy(showSuccess: true);
        AppendStatus("Guided setup changes applied to JSON editor.");
    }

    private void AddProjectButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!TryGetPolicy(out var policy))
        {
            return;
        }

        var wizard = new ProjectTemplateWizardWindow
        {
            Owner = this
        };

        if (wizard.ShowDialog() != true || wizard.ProjectTemplate is null)
        {
            return;
        }

        var existingProject = policy!.Projects.FirstOrDefault(project =>
            project.ProjectId.Equals(wizard.ProjectTemplate.ProjectId, StringComparison.OrdinalIgnoreCase));
        if (existingProject is not null)
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

            policy.Projects.Remove(existingProject);
        }

        policy.Projects.Add(wizard.ProjectTemplate);
        PolicyJsonTextBox.Text = PolicyEditorUtility.SerializePolicy(policy);
        ValidateCurrentPolicy(showSuccess: true);
        AppendStatus($"Project template '{wizard.ProjectTemplate.ProjectId}' added to JSON editor.");
    }

    private async void SaveButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        await SaveAsync(sign: false, reload: false);
    }

    private async void SaveSignReloadButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        await SaveAsync(sign: true, reload: true);
    }

    private async void CheckStructureButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!TryGetPolicy(out var policy))
        {
            return;
        }

        if (policy!.Projects.Count == 0)
        {
            AppendStatus("No projects found for structure check.");
            return;
        }

        var summaryLines = new List<string>();
        foreach (var project in policy.Projects)
        {
            var report = await _projectStructureAuditor.CheckAsync(project, CancellationToken.None);
            summaryLines.Add(
                $"{report.ProjectId}: Exists={report.ExistsCount}, Missing={report.MissingCount}, OutsideRoot={report.OutsideRootCount}, AccessDenied={report.AccessDeniedCount}, Invalid={report.InvalidCount}");
        }

        AppendStatus($"Project structure check complete: {string.Join(" | ", summaryLines)}");
    }

    private async void BuildDemoMirrorButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var snapshot = await _policyConfigManager.GetSnapshotAsync(CancellationToken.None);
        if (snapshot.Policy.Projects.Count == 0)
        {
            AppendStatus("No projects found for demo mirror refresh.");
            return;
        }

        if (string.IsNullOrWhiteSpace(snapshot.UserPreferences.DemoMirrorFolderName))
        {
            snapshot.UserPreferences.DemoMirrorFolderName = "_FRA_Demo";
        }

        foreach (var project in snapshot.Policy.Projects)
        {
            var projectRoot = DemoModeStateFactory.ResolveProjectRoot(project, _pathCanonicalizer);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                continue;
            }

            var mirrorRoot = _pathCanonicalizer.Canonicalize(
                Path.Combine(projectRoot, snapshot.UserPreferences.DemoMirrorFolderName));
            snapshot.UserPreferences.DemoMirrorRootsByProject[project.ProjectId] = mirrorRoot;
        }

        var state = DemoModeStateFactory.Resolve(snapshot, _pathCanonicalizer) with { Enabled = true };
        var statusLines = new List<string>();
        foreach (var project in snapshot.Policy.Projects)
        {
            var result = await _demoMirrorService.RefreshAsync(project, state, CancellationToken.None);
            statusLines.Add(
                $"{result.ProjectId}: Created={result.CreatedCount}, Existing={result.ExistingCount}, Skipped={result.SkippedCount}, Errors={result.Errors.Count}");
        }

        snapshot.UserPreferences.LastDemoMirrorRefreshUtc = DateTime.UtcNow;
        await _policyConfigManager.SaveUserPreferencesAsync(snapshot.UserPreferences, CancellationToken.None);
        var refreshed = await _runtimePolicyRefresher.RefreshAsync(CancellationToken.None);
        _snapshotAccessor.Update(refreshed);

        AppendStatus($"Demo mirror refresh complete: {string.Join(" | ", statusLines)}");
    }

    private async void ToggleDemoModeButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var snapshot = await _policyConfigManager.GetSnapshotAsync(CancellationToken.None);
        if (string.IsNullOrWhiteSpace(snapshot.UserPreferences.DemoMirrorFolderName))
        {
            snapshot.UserPreferences.DemoMirrorFolderName = "_FRA_Demo";
        }

        foreach (var project in snapshot.Policy.Projects)
        {
            var projectRoot = DemoModeStateFactory.ResolveProjectRoot(project, _pathCanonicalizer);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                continue;
            }

            var mirrorRoot = _pathCanonicalizer.Canonicalize(
                Path.Combine(projectRoot, snapshot.UserPreferences.DemoMirrorFolderName));
            snapshot.UserPreferences.DemoMirrorRootsByProject[project.ProjectId] = mirrorRoot;
        }

        snapshot.UserPreferences.DemoModeEnabled = !snapshot.UserPreferences.DemoModeEnabled;
        await _policyConfigManager.SaveUserPreferencesAsync(snapshot.UserPreferences, CancellationToken.None);
        var refreshed = await _runtimePolicyRefresher.RefreshAsync(CancellationToken.None);
        _snapshotAccessor.Update(refreshed);

        AppendStatus($"Demo mode is now {(snapshot.UserPreferences.DemoModeEnabled ? "ON (Mirror Only)" : "OFF")}.");
    }

    private void ApplyProjectWiseProfileButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!TryGetPolicy(out var policy))
        {
            return;
        }

        if (policy!.Projects.Count == 0)
        {
            AppendStatus("No projects available to apply ProjectWise command profile.");
            return;
        }

        var updated = 0;
        var skipped = 0;
        foreach (var project in policy.Projects)
        {
            if (project.Connector.Enabled)
            {
                skipped++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(project.Connector.Provider) &&
                !project.Connector.Provider.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                !PolicyEditorUtility.IsCommandConnectorProvider(project.Connector.Provider))
            {
                skipped++;
                continue;
            }

            var existingSettings = project.Connector.Settings;
            project.Connector = PolicyEditorUtility.BuildProjectWiseCommandProfile(enabled: true);
            foreach (var (key, value) in existingSettings)
            {
                project.Connector.Settings[key] = value;
            }
            updated++;
        }

        PolicyJsonTextBox.Text = PolicyEditorUtility.SerializePolicy(policy);
        ValidateCurrentPolicy(showSuccess: false);
        AppendStatus($"ProjectWise command profile applied. Updated: {updated}, skipped: {skipped}.");
    }

    private void CloseButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }

    private async Task LoadFromDiskAsync()
    {
        var snapshot = _snapshotAccessor.Snapshot;
        _policyPath = snapshot?.PolicyPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_policyPath))
        {
            System.Windows.MessageBox.Show(
                "Policy path is unavailable.",
                "Configuration Editor",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (!File.Exists(_policyPath))
        {
            PolicyJsonTextBox.Text = string.Empty;
            ValidationListBox.ItemsSource = new[] { "Policy file does not exist." };
            return;
        }

        PolicyJsonTextBox.Text = await File.ReadAllTextAsync(_policyPath);
        ValidateCurrentPolicy(showSuccess: false);
        AppendStatus("Policy JSON reloaded from disk.");
    }

    private bool TryGetPolicy(out FirmPolicy? policy)
    {
        if (!PolicyEditorUtility.TryParsePolicy(PolicyJsonTextBox.Text, out policy, out var parseError))
        {
            ValidationListBox.ItemsSource = new[] { $"Parse error: {parseError}" };
            AppendStatus($"Parse error: {parseError}");
            return false;
        }

        return true;
    }

    private bool ValidateCurrentPolicy(bool showSuccess)
    {
        if (!TryGetPolicy(out var policy))
        {
            return false;
        }

        var validationErrors = PolicyEditorUtility.ValidatePolicy(policy!);
        ValidationListBox.ItemsSource = validationErrors.Count == 0
            ? new[] { "Validation passed." }
            : validationErrors;

        _signaturePath = PolicyEditorUtility.ResolveSignaturePath(policy!, _policyPath);
        UpdateHeader(policy!, validationErrors.Count == 0);

        if (validationErrors.Count == 0 && showSuccess)
        {
            AppendStatus("Validation passed.");
        }
        else if (validationErrors.Count > 0)
        {
            AppendStatus($"Validation failed with {validationErrors.Count} issue(s).");
        }

        return validationErrors.Count == 0;
    }

    private async Task SaveAsync(bool sign, bool reload)
    {
        if (!ValidateCurrentPolicy(showSuccess: false))
        {
            System.Windows.MessageBox.Show(
                "Cannot save: validation failed.",
                "Validation Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        await File.WriteAllTextAsync(_policyPath, PolicyJsonTextBox.Text);
        AppendStatus($"Policy saved to '{_policyPath}'.");

        if (sign)
        {
            var signature = PolicyEditorUtility.ComputeSha256Hex(_policyPath);
            await File.WriteAllTextAsync(_signaturePath, signature);
            AppendStatus($"Signature updated at '{_signaturePath}'.");
        }

        if (reload)
        {
            var refreshed = await _runtimePolicyRefresher.RefreshAsync(CancellationToken.None);
            _snapshotAccessor.Update(refreshed);
            AppendStatus(
                refreshed.SafeModeEnabled
                    ? $"Policy reloaded in SAFE MODE: {refreshed.SafeModeReason}"
                    : "Policy reloaded successfully.");
        }

        ValidateCurrentPolicy(showSuccess: false);
    }

    private void UpdateHeader(FirmPolicy policy, bool valid)
    {
        PolicyPathText.Text = $"Policy: {_policyPath}";
        SignaturePathText.Text = $"Signature: {_signaturePath}";

        var signatureState = File.Exists(_signaturePath)
            ? PolicyEditorUtility.SignatureMatches(_policyPath, _signaturePath)
                ? "Signature status: valid"
                : "Signature status: mismatch"
            : "Signature status: missing";

        SignatureStatusText.Text = $"{signatureState} | Validation: {(valid ? "passed" : "failed")}";
    }

    private void AppendStatus(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (string.IsNullOrWhiteSpace(StatusTextBox.Text))
        {
            StatusTextBox.Text = line;
            return;
        }

        StatusTextBox.AppendText(Environment.NewLine + line);
        StatusTextBox.ScrollToEnd();
    }
}
