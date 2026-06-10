using System.Diagnostics;
using ItrqTool.Domain;

namespace ItrqTool.Tasks;

/// <summary>
/// Trial-infrastructure task. Exposes a pre-existing file as a named task output
/// so that downstream tasks (e.g. <c>FeedbackChecklistAssembler</c>) can consume it
/// through the standard input-path mechanism.
/// A relative <c>sourcePath</c> is resolved against <c>AppContext.BaseDirectory</c>;
/// an absolute path is used as-is.
/// </summary>
public sealed class StaticFileSourceTask : IWorkflowTask
{
    public string TaskType => "StaticFileSource";

    public async Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        try
        {
            if (!ctx.Parameters.TryGetValue("sourcePath", out var raw) || string.IsNullOrEmpty(raw))
            {
                messages.Add(new(MessageSeverity.Error,
                    "Required parameter missing or empty: sourcePath.", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            var sourcePath = Path.IsPathRooted(raw)
                ? raw
                : Path.Combine(AppContext.BaseDirectory, raw);

            if (!File.Exists(sourcePath))
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Source file not found: {sourcePath}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            var outputPath = ctx.OutputPaths["output"];
            await using var src = new FileStream(
                sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            await using var dst = new FileStream(
                outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
            await src.CopyToAsync(dst, ct);

            messages.Add(new(MessageSeverity.Info,
                $"Copied {Path.GetFileName(sourcePath)} → {Path.GetFileName(outputPath)}.",
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
