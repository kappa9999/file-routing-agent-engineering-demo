using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using StorageAudit.Tool;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using MessageBox = System.Windows.MessageBox;

namespace FolderCompare.App;

public partial class MainWindow : Window
{
    private readonly FolderCompareSettingsStore _settingsStore = new();
    private bool _isRunning;
    private string? _latestOutputFolder;
    private string? _latestWorkbookPath;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySettings(await _settingsStore.LoadAsync());
        AppendStatus("Ready. Select two folders and click Run Compare.");
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settingsStore.SaveAsync(BuildSettingsSnapshot()).GetAwaiter().GetResult();
    }

    private async void RunCompareButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            return;
        }

        var primaryFolder = PrimaryFolderTextBox.Text.Trim();
        var referenceFolder = ReferenceFolderTextBox.Text.Trim();
        var outputFolder = OutputFolderTextBox.Text.Trim();
        var cutoffDate = CutoffDatePicker.SelectedDate;

        if (string.IsNullOrWhiteSpace(primaryFolder) || !Directory.Exists(primaryFolder))
        {
            MessageBox.Show(this, "Select a valid Primary Folder before running the compare.", "Folder Compare Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(referenceFolder) || !Directory.Exists(referenceFolder))
        {
            MessageBox.Show(this, "Select a valid Reference Folder before running the compare.", "Folder Compare Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (PathsEqual(primaryFolder, referenceFolder))
        {
            MessageBox.Show(this, "Primary Folder and Reference Folder must be different.", "Folder Compare Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!cutoffDate.HasValue)
        {
            MessageBox.Show(this, "Select a cutoff date before running the compare.", "Folder Compare Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var cutoffLocal = new DateTimeOffset(
            DateTime.SpecifyKind(cutoffDate.Value.Date, DateTimeKind.Unspecified),
            TimeZoneInfo.Local.GetUtcOffset(cutoffDate.Value.Date));

        var options = new ProjectWiseReconcileOptions
        {
            PDriveRootPath = primaryFolder,
            CompareRootPath = referenceFolder,
            OutputFolder = string.IsNullOrWhiteSpace(outputFolder) ? null : outputFolder,
            CutoffLocal = cutoffLocal
        };

        await _settingsStore.SaveAsync(BuildSettingsSnapshot());

        _isRunning = true;
        _latestOutputFolder = null;
        _latestWorkbookPath = null;
        UpdateUiState();
        AppendStatus(string.Empty);
        AppendStatus($"Starting compare at {DateTime.Now:G}");
        AppendStatus($"Primary Folder:   {primaryFolder}");
        AppendStatus($"Reference Folder: {referenceFolder}");
        AppendStatus($"Cutoff Date:      {cutoffDate.Value:yyyy-MM-dd}");

        try
        {
            var runner = new ProjectWiseReconcileRunner();
            var result = await Task.Run(() => runner.RunAsync(options, CancellationToken.None));

            _latestOutputFolder = result.Summary.OutputFolder;
            _latestWorkbookPath = result.Artifacts.WorkbookPath;
            LatestOutputTextBlock.Text = $"Latest output: {_latestOutputFolder}";

            AppendStatus("Compare completed successfully.");
            AppendStatus($"Primary files scanned:   {result.Summary.PDriveFilesScanned:N0}");
            AppendStatus($"Reference files scanned: {result.Summary.CompareFilesScanned:N0}");
            AppendStatus($"Missing from reference:  {result.Summary.MissingFromPwCount:N0}");
            AppendStatus($"Changed after cutoff:    {result.Summary.ChangedAfterCutoffCount:N0}");
            AppendStatus($"Cleanup review:          {result.Summary.CleanupReviewCount:N0}");
            AppendStatus($"Ambiguous matches:       {result.Summary.AmbiguousCount:N0}");
            AppendStatus($"Scan issues:             {result.Summary.CompareIssueCount:N0}");
            AppendStatus($"Workbook: {_latestWorkbookPath}");

            OpenOutputButton.IsEnabled = !string.IsNullOrWhiteSpace(_latestOutputFolder);
            OpenWorkbookButton.IsEnabled = !string.IsNullOrWhiteSpace(_latestWorkbookPath);

            if (OpenOutputOnSuccessCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(_latestOutputFolder))
            {
                OpenPath(_latestOutputFolder);
            }
        }
        catch (Exception exception)
        {
            LatestOutputTextBlock.Text = "Latest output: run failed";
            AppendStatus($"Compare failed: {exception.Message}");
            MessageBox.Show(this, exception.Message, "Folder Compare Tool", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isRunning = false;
            UpdateUiState();
        }
    }

    private void BrowsePrimaryFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = BrowseForFolder("Select the primary folder to scan", PrimaryFolderTextBox.Text);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            PrimaryFolderTextBox.Text = selected;
        }
    }

    private void BrowseReferenceFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = BrowseForFolder("Select the reference folder to compare against", ReferenceFolderTextBox.Text);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            ReferenceFolderTextBox.Text = selected;
        }
    }

    private void BrowseOutputFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = BrowseForFolder("Select an output folder for the report package", OutputFolderTextBox.Text);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            OutputFolderTextBox.Text = selected;
        }
    }

    private void ClearOutputFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        OutputFolderTextBox.Text = string.Empty;
    }

    private void UseRecommendedDefaultsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplySettings(new FolderCompareSettings());
        AppendStatus("Recommended defaults loaded.");
    }

    private void OpenOutputButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_latestOutputFolder))
        {
            OpenPath(_latestOutputFolder);
        }
    }

    private void OpenWorkbookButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_latestWorkbookPath))
        {
            OpenPath(_latestWorkbookPath);
        }
    }

    private void ApplySettings(FolderCompareSettings settings)
    {
        PrimaryFolderTextBox.Text = settings.PrimaryFolderPath;
        ReferenceFolderTextBox.Text = settings.ReferenceFolderPath;
        OutputFolderTextBox.Text = settings.OutputFolderPath;
        CutoffDatePicker.SelectedDate = settings.CutoffDateLocal.Date;
        OpenOutputOnSuccessCheckBox.IsChecked = settings.OpenOutputOnSuccess;
    }

    private FolderCompareSettings BuildSettingsSnapshot()
    {
        return new FolderCompareSettings
        {
            PrimaryFolderPath = PrimaryFolderTextBox.Text.Trim(),
            ReferenceFolderPath = ReferenceFolderTextBox.Text.Trim(),
            OutputFolderPath = OutputFolderTextBox.Text.Trim(),
            CutoffDateLocal = (CutoffDatePicker.SelectedDate ?? new DateTime(2025, 4, 1)).Date,
            OpenOutputOnSuccess = OpenOutputOnSuccessCheckBox.IsChecked == true
        };
    }

    private void UpdateUiState()
    {
        RunCompareButton.IsEnabled = !_isRunning;
        RunProgressBar.Visibility = _isRunning ? Visibility.Visible : Visibility.Collapsed;
        OpenOutputButton.IsEnabled = !_isRunning && !string.IsNullOrWhiteSpace(_latestOutputFolder);
        OpenWorkbookButton.IsEnabled = !_isRunning && !string.IsNullOrWhiteSpace(_latestWorkbookPath);
    }

    private void AppendStatus(string message)
    {
        var builder = new StringBuilder(StatusTextBox.Text);
        if (!string.IsNullOrEmpty(message))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(message);
        }
        else if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        StatusTextBox.Text = builder.ToString();
        StatusTextBox.CaretIndex = StatusTextBox.Text.Length;
        StatusTextBox.ScrollToEnd();
    }

    private static string? BrowseForFolder(string description, string currentPath)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(currentPath) ? currentPath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
