using FileRoutingAgent.App.Services;
using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;

namespace FileRoutingAgent.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FileRoutingAgent");
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(Path.Combine(appData, "Logs"));

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(appData, "Logs", "agent-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        _host = Host.CreateDefaultBuilder(e.Args)
            .UseSerilog()
            .ConfigureAppConfiguration(configuration =>
            {
                configuration.Sources.Clear();
                configuration.SetBasePath(AppContext.BaseDirectory);
                configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                configuration.AddEnvironmentVariables(prefix: "FILEROUTINGAGENT_");
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<AgentRuntimeOptions>(context.Configuration.GetSection("AgentRuntime"));
                services.Configure<AutomationPromptOptions>(context.Configuration.GetSection("AutomationPrompt"));
                services.PostConfigure<AgentRuntimeOptions>(NormalizeRuntimeOptions);

                services.AddSingleton<IUserPromptService, DesktopPromptService>();
                services.AddSingleton<SupportBundleService>();
                services.AddHostedService<TrayShellHostedService>();
                services.AddFileRoutingInfrastructure();
            })
            .Build();

        await _host.StartAsync();
    }

    protected override async void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(10));
            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void NormalizeRuntimeOptions(AgentRuntimeOptions options)
    {
        options.PolicyPath = NormalizePath(options.PolicyPath);
        options.UserPreferencesPath = NormalizePath(options.UserPreferencesPath);
        options.DatabasePath = NormalizePath(options.DatabasePath);
    }

    private static string NormalizePath(string value)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value);
        if (Path.IsPathRooted(expanded))
        {
            return expanded;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, expanded));
    }
}
