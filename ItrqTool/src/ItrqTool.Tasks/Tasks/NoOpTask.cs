using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;

namespace ItrqTool.Tasks;

public sealed class NoOpTask : IWorkflowTask
{
    private readonly ILogger<NoOpTask> _logger;

    public NoOpTask(ILogger<NoOpTask> logger) => _logger = logger;

    public string TaskType => "NoOp";

    public async Task<TaskResult> ExecuteAsync(
        TaskExecutionContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var messages = new List<TaskMessage>();

        try
        {
            _logger.LogInformation(
                "Starting. TaskId={TaskId}, Inputs={InputCount}, Outputs={OutputCount}",
                context.TaskId, context.InputPaths.Count, context.OutputPaths.Count);

            foreach (var (key, path) in context.OutputPaths)
            {
                ct.ThrowIfCancellationRequested();
                await File.WriteAllTextAsync(path, string.Empty, ct);
                _logger.LogInformation(
                    "Wrote empty output '{Key}' to {File}",
                    key, Path.GetFileName(path));
            }

            messages.Add(new(MessageSeverity.Info,
                $"NoOp completed: wrote {context.OutputPaths.Count} output file(s).",
                DateTimeOffset.Now));

            return new TaskResult(Succeeded: true, messages, sw.Elapsed);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            messages.Add(new(MessageSeverity.Error, ex.Message, DateTimeOffset.Now));
            return new TaskResult(Succeeded: false, messages, sw.Elapsed);
        }
    }
}
