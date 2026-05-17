using Microsoft.Extensions.Logging;
using ItrqTool.Domain;

namespace ItrqTool.Application;

public sealed class WorkflowSessionFactory
{
    private readonly string _workflowDataRoot;
    private readonly ITaskRegistry _taskRegistry;
    private readonly ILogger<WorkflowSession> _logger;

    public WorkflowSessionFactory(
        string workflowDataRoot,
        ITaskRegistry taskRegistry,
        ILogger<WorkflowSession> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDataRoot);
        ArgumentNullException.ThrowIfNull(taskRegistry);
        ArgumentNullException.ThrowIfNull(logger);

        _workflowDataRoot = workflowDataRoot;
        _taskRegistry = taskRegistry;
        _logger = logger;
    }

    public WorkflowSession Create(WorkflowDefinition workflow)
    {
        var workingDirectory = Path.Combine(_workflowDataRoot, workflow.Id);
        return new WorkflowSession(workflow, _taskRegistry, workingDirectory, _logger);
    }
}
