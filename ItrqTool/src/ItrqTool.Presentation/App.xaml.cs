using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ItrqTool.Presentation.ViewModels;

namespace ItrqTool.Presentation;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information));

        var workflowsPath = Path.Combine(AppContext.BaseDirectory, "workflows");
        services.AddItrqToolServices(workflowsPath);

        _services = services.BuildServiceProvider();

        var mainWindow = new Views.MainWindow(_services.GetRequiredService<WorkflowListViewModel>());
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
