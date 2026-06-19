using System.Diagnostics;
using ItrqTool.Domain;

namespace ItrqTool.Tasks;

/// <summary>
/// Terminal task. Copies a working-dir input file to an external destination folder
/// under a caller-specified file name. Dual of <c>StaticFileSource</c>.
/// A relative <c>destinationFolder</c> is resolved against <c>AppContext.BaseDirectory</c>;
/// an absolute path is used as-is. The destination folder is created if absent.
/// </summary>
public sealed class StaticFileSinkTask : IWorkflowTask
{
    public string TaskType => "StaticFileSink";

    public async Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        try
        {
            if (!ctx.InputPaths.TryGetValue("input", out var inputPath) || string.IsNullOrEmpty(inputPath))
            {
                messages.Add(new(MessageSeverity.Error,
                    "Required input missing or empty: input.", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            if (!ctx.Parameters.TryGetValue("destinationFolder", out var rawFolder) || string.IsNullOrEmpty(rawFolder))
            {
                messages.Add(new(MessageSeverity.Error,
                    "Required parameter missing or empty: destinationFolder.", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            if (!ctx.Parameters.TryGetValue("destinationFileName", out var fileName) || string.IsNullOrEmpty(fileName))
            {
                messages.Add(new(MessageSeverity.Error,
                    "Required parameter missing or empty: destinationFileName.", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            if (!File.Exists(inputPath))
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Input file not found: {inputPath}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            var resolvedFolder = Path.IsPathRooted(rawFolder)
                ? rawFolder
                : Path.Combine(AppContext.BaseDirectory, rawFolder);

            var destination = Path.Combine(resolvedFolder, fileName);

            Directory.CreateDirectory(resolvedFolder);

            await using var src = new FileStream(
                inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            await using var dst = new FileStream(
                destination, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
            await src.CopyToAsync(dst, ct);

            messages.Add(new(MessageSeverity.Info,
                $"Placed {Path.GetFileName(inputPath)} → {destination}.",
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
