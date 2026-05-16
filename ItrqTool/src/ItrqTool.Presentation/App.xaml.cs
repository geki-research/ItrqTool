using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ItrqTool.Presentation.Views;
using Serilog;

namespace ItrqTool.Presentation;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var workflowsPath = Path.Combine(AppContext.BaseDirectory, "workflows");

        var configuredRoot = config["ItrqTool:WorkflowDataRoot"];
        var workflowDataRoot = Environment.ExpandEnvironmentVariables(
            string.IsNullOrWhiteSpace(configuredRoot)
                ? @"%USERPROFILE%\Documents\ItrqTool"
                : configuredRoot);
        Directory.CreateDirectory(workflowDataRoot);

        var configuredLogs = config["ItrqTool:LogsDirectory"];
        var logsDirectory = Environment.ExpandEnvironmentVariables(
            string.IsNullOrWhiteSpace(configuredLogs)
                ? @"%USERPROFILE%\Documents\ItrqTool\logs"
                : configuredLogs);

        try
        {
            Directory.CreateDirectory(logsDirectory);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    Path.Combine(logsDirectory, "itrqtool-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate:
                        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                    flushToDiskInterval: TimeSpan.FromSeconds(1))
                .CreateLogger();
        }
        catch
        {
            // file logging unavailable — app continues without it
        }

        var services = new ServiceCollection();
        services.AddItrqToolServices(workflowsPath, workflowDataRoot);

        _services = services.BuildServiceProvider();

        var mainWindow = _services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
