using Microsoft.Extensions.Logging;
using ItrqTool.Domain;

namespace ItrqTool.Application;

public sealed class WorkflowSession
{
    private readonly ITaskRegistry _taskRegistry;
    private readonly ILogger<WorkflowSession> _logger;
    private readonly List<TaskResult?> _results;

    public WorkflowDefinition Workflow { get; }
    public WorkflowSessionStatus Status { get; private set; }
    public int CurrentIndex { get; private set; }
    public string WorkingDirectory { get; }

    public WorkflowSession(
        WorkflowDefinition workflow,
        ITaskRegistry taskRegistry,
        ILogger<WorkflowSession> logger)
    {
        Workflow = workflow;
        _taskRegistry = taskRegistry;
        _logger = logger;
        _results = new List<TaskResult?>(new TaskResult?[workflow.Nodes.Count]);
        WorkingDirectory = Path.Combine(Path.GetTempPath(), "ItrqTool", $"run_{DateTime.UtcNow:yyyyMMddHHmmssfff}");
        Status = WorkflowSessionStatus.ReadyToRun;
    }

    public TaskResult? GetResult(int index)
        => index >= 0 && index < _results.Count ? _results[index] : null;

    public Task<TaskResult> RunCurrentTaskAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
}
