using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AppLayer = ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Infrastructure;
using ItrqTool.Presentation.ViewModels;
using ItrqTool.Tasks;

namespace ItrqTool.Presentation;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information));

        var workflowsDir = Path.Combine(AppContext.BaseDirectory, "workflows");
        services.AddSingleton<IExcelReader, ClosedXmlExcelReader>();
        services.AddSingleton<IWorkflowLoader>(_ => new JsonWorkflowLoader(workflowsDir));

        services.AddSingleton<AppLayer.WorkflowSessionFactory>();
        services.AddSingleton<AppLayer.ITaskRegistry, AppLayer.DependencyInjectionTaskRegistry>();

        services.Scan(scan => scan
            .FromAssemblyOf<IWorkflowTaskMarker>()
            .AddClasses(c => c.AssignableTo<IWorkflowTask>())
            .AsImplementedInterfaces()
            .WithTransientLifetime());

        services.AddTransient<WorkflowRunViewModel>();
        services.AddTransient<WorkflowListViewModel>();

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
