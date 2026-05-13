using ItrqTool.Domain;

namespace ItrqTool.Application;

public sealed class DependencyInjectionTaskRegistry : ITaskRegistry
{
    private readonly IReadOnlyDictionary<string, IWorkflowTask> _tasks;

    public DependencyInjectionTaskRegistry(IEnumerable<IWorkflowTask> tasks)
        => _tasks = tasks.ToDictionary(t => t.TaskType);

    public IWorkflowTask? FindTask(string taskType)
        => _tasks.GetValueOrDefault(taskType);
}
