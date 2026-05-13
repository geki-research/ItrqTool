using System.Diagnostics;
using ItrqTool.Domain;

namespace ItrqTool.Tasks;

public sealed class NoOpTask : IWorkflowTask
{
    public string TaskType => "NoOp";

    public async Task<TaskResult> ExecuteAsync(
        TaskExecutionContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var messages = new List<TaskMessage>();

        try
        {
            messages.Add(new(MessageSeverity.Info,
                $"NoOpTask started. TaskId={context.TaskId}, " +
                $"InputPaths={context.InputPaths.Count}, " +
                $"OutputPaths={context.OutputPaths.Count}",
                DateTimeOffset.Now));

            foreach (var (key, path) in context.OutputPaths)
            {
                ct.ThrowIfCancellationRequested();
                await File.WriteAllTextAsync(path, string.Empty, ct);
                messages.Add(new(MessageSeverity.Info,
                    $"Wrote empty output '{key}' → {Path.GetFileName(path)}",
                    DateTimeOffset.Now));
            }

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
