namespace ItrqTool.Domain;

public interface IWorkflowTask
{
    string TaskType { get; }
    Task<TaskResult> ExecuteAsync(TaskExecutionContext context, CancellationToken ct);
}
