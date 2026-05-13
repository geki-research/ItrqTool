using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Infrastructure;
using ItrqTool.Presentation.ViewModels;
using ItrqTool.Tasks;

namespace ItrqTool.Presentation;

public static class CompositionRoot
{
    public static IServiceCollection AddItrqToolServices(
        this IServiceCollection services,
        string workflowsDirectoryPath)
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

        services.AddSingleton<WorkflowSessionFactory>();
        services.AddSingleton<ITaskRegistry, DependencyInjectionTaskRegistry>();

        services.Scan(scan => scan
            .FromAssemblyOf<IWorkflowTaskMarker>()
            .AddClasses(c => c.AssignableTo<IWorkflowTask>())
            .AsImplementedInterfaces()
            .WithTransientLifetime());

        services.AddTransient<WorkflowRunViewModel>();
        services.AddTransient<WorkflowListViewModel>();

        return services;
    }
}
