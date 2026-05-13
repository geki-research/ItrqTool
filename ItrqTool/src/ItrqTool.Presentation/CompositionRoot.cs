using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Infrastructure;
using ItrqTool.Presentation.ViewModels;
using ItrqTool.Presentation.Views;
using ItrqTool.Tasks;

namespace ItrqTool.Presentation;

public static class CompositionRoot
{
    public static IServiceCollection AddItrqToolServices(
        this IServiceCollection services,
        string workflowsDirectoryPath,
        string workflowDataRoot)
    {
        // TryAdd so production code can pre-register Serilog before calling this method
        // and tests get NullLogger without any extra setup.
        services.TryAddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.TryAddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

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

        services.AddSingleton<WorkflowSessionFactory>(sp =>
            new WorkflowSessionFactory(
                workflowDataRoot,
                sp.GetRequiredService<ITaskRegistry>(),
                sp.GetRequiredService<ILogger<WorkflowSession>>()));

        services.AddSingleton<WorkflowListViewModel>();
        services.AddSingleton<WorkflowRunViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
