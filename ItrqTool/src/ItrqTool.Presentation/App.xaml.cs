using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ItrqTool.Presentation.Views;

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

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information));
        services.AddItrqToolServices(workflowsPath, workflowDataRoot);

        _services = services.BuildServiceProvider();

        var mainWindow = _services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
