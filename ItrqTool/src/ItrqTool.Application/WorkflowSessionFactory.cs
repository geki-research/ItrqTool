using Microsoft.Extensions.Logging;
using ItrqTool.Domain;

namespace ItrqTool.Application;

public sealed class WorkflowSessionFactory
{
    private readonly ITaskRegistry _taskRegistry;
    private readonly ILogger<WorkflowSession> _logger;

    public WorkflowSessionFactory(ITaskRegistry taskRegistry, ILogger<WorkflowSession> logger)
    {
        _taskRegistry = taskRegistry;
        _logger = logger;
    }

    public WorkflowSession Create(WorkflowDefinition workflow)
    {
        var workingDirectory = Path.Combine(
            Path.GetTempPath(), "ItrqTool", $"run_{DateTime.UtcNow:yyyyMMddHHmmssfff}");
        return new WorkflowSession(workflow, _taskRegistry, workingDirectory, _logger);
    }
}
