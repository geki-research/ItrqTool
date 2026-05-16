using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Infrastructure;
using ItrqTool.Presentation.Logging;
using ItrqTool.Presentation.ViewModels;
using ItrqTool.Presentation.Views;
using ItrqTool.Tasks;
using Serilog;

namespace ItrqTool.Presentation;

public static class CompositionRoot
{
    // Primary two-param overload — used by App.xaml.cs (workflowDataRoot reserved for future use).
    public static IServiceCollection AddItrqToolServices(
        this IServiceCollection services,
        string workflowsDirectoryPath,
        string workflowDataRoot)
        => services.AddItrqToolServices(workflowsDirectoryPath);

    // Single-param overload — used by integration tests.
    public static IServiceCollection AddItrqToolServices(
        this IServiceCollection services,
        string workflowsDirectoryPath)
    {
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        services.AddSingleton<IUiLogSink>(_ =>
            new UiLogSink(System.Windows.Application.Current?.Dispatcher));
        services.AddSingleton<ILoggerProvider>(sp =>
            new UiLogSinkProvider(sp.GetRequiredService<IUiLogSink>()));

        services.AddSingleton<IExcelReader, ClosedXmlExcelReader>();
        services.AddSingleton<IWorkflowLoader>(sp =>
            new JsonWorkflowLoader(
                workflowsDirectoryPath,
                sp.GetRequiredService<ILogger<JsonWorkflowLoader>>()));

        services.Scan(scan => scan
            .FromAssemblyOf<IWorkflowTaskMarker>()
            .AddClasses(c => c.AssignableTo<IWorkflowTask>())
            .AsImplementedInterfaces()
            .WithTransientLifetime());

        services.AddSingleton<ITaskRegistry, DependencyInjectionTaskRegistry>();
        services.AddSingleton<WorkflowSessionFactory>();

        services.AddSingleton<WorkflowListViewModel>();
        services.AddSingleton<WorkflowRunViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
