using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;

namespace ItrqTool.Application;

public sealed class WorkflowSession
{
    private readonly ITaskRegistry _taskRegistry;
    private readonly ILogger<WorkflowSession> _logger;
    private readonly WorkflowGraph _graph;
    private readonly IReadOnlyList<TaskNode> _topologicalOrder;
    private readonly List<TaskResult?> _results;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _recordedOutputPaths = new();
    private bool _hasExecutedFirstTask;

    public WorkflowDefinition Workflow { get; }
    public WorkflowSessionStatus Status { get; private set; }
    public int CurrentIndex { get; private set; }
    public string WorkingDirectory { get; }

    public WorkflowSession(
        WorkflowDefinition workflow,
        ITaskRegistry taskRegistry,
        string workingDirectory,
        ILogger<WorkflowSession> logger)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(taskRegistry);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        Workflow = workflow;
        _taskRegistry = taskRegistry;
        WorkingDirectory = workingDirectory;
        _logger = logger;

        _graph = new WorkflowGraph(workflow);
        _topologicalOrder = _graph.GetTopologicalOrder();
        _results = new List<TaskResult?>(new TaskResult?[_topologicalOrder.Count]);

        Status = WorkflowSessionStatus.ReadyToRun;
        CurrentIndex = 0;
    }

    public TaskResult? GetResult(int index)
        => index >= 0 && index < _results.Count ? _results[index] : null;

    public async Task<TaskResult> RunCurrentTaskAsync(CancellationToken ct = default)
    {
        if (Status != WorkflowSessionStatus.ReadyToRun && Status != WorkflowSessionStatus.AwaitingReview)
            throw new InvalidOperationException(
                $"Cannot run a task when session status is '{Status}'.");

        var currentNode = _topologicalOrder[CurrentIndex];

        var task = _taskRegistry.FindTask(currentNode.TaskType);
        if (task is null)
        {
            var notFound = new TaskResult(
                Succeeded: false,
                Messages: new[] { new TaskMessage(
                    MessageSeverity.Error,
                    $"No task registered for type '{currentNode.TaskType}'",
                    DateTimeOffset.Now) },
                Duration: TimeSpan.Zero);
            _results[CurrentIndex] = notFound;
            Status = WorkflowSessionStatus.Failed;
            return notFound;
        }

        Status = WorkflowSessionStatus.Running;

        if (!_hasExecutedFirstTask)
        {
            try
            {
                if (Directory.Exists(WorkingDirectory))
                {
                    foreach (var file in Directory.EnumerateFiles(WorkingDirectory))
                        File.Delete(file);
                    foreach (var dir in Directory.EnumerateDirectories(WorkingDirectory))
                        Directory.Delete(dir, recursive: true);
                }
                else
                {
                    Directory.CreateDirectory(WorkingDirectory);
                }
                _hasExecutedFirstTask = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to prepare working directory '{Path}'",
                                 WorkingDirectory);
                var prepFailure = new TaskResult(
                    Succeeded: false,
                    Messages: new[] { new TaskMessage(
                        MessageSeverity.Error,
                        $"Failed to prepare working directory: {ex.Message}",
                        DateTimeOffset.Now) },
                    Duration: TimeSpan.Zero);
                _results[CurrentIndex] = prepFailure;
                Status = WorkflowSessionStatus.Failed;
                return prepFailure;
            }
        }

        var inputPaths = new Dictionary<string, string>();
        foreach (var (localKey, outputRef) in currentNode.Inputs)
        {
            if (_recordedOutputPaths.TryGetValue(outputRef.TaskId, out var upstreamOutputs) &&
                upstreamOutputs.TryGetValue(outputRef.OutputKey, out var resolvedPath))
            {
                inputPaths[localKey] = resolvedPath;
            }
        }

        var outputPaths = new Dictionary<string, string>();
        foreach (var (localKey, fileName) in currentNode.OutputFileNames)
            outputPaths[localKey] = Path.Combine(WorkingDirectory, fileName);

        _recordedOutputPaths[currentNode.Id] = outputPaths;

        var context = new TaskExecutionContext(
            TaskId: currentNode.Id,
            InputPaths: inputPaths,
            OutputPaths: outputPaths,
            Logger: _logger,
            WorkingDirectory: WorkingDirectory)
        {
            Parameters = currentNode.Parameters
        };

        var sw = Stopwatch.StartNew();

        try
        {
            var result = await task.ExecuteAsync(context, ct);
            sw.Stop();

            _results[CurrentIndex] = result;

            if (!result.Succeeded)
            {
                Status = WorkflowSessionStatus.Failed;
                return result;
            }

            CurrentIndex++;
            Status = CurrentIndex == _topologicalOrder.Count
                ? WorkflowSessionStatus.Completed
                : WorkflowSessionStatus.AwaitingReview;

            return result;
        }
        catch (OperationCanceledException)
        {
            Status = WorkflowSessionStatus.ReadyToRun;
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Task '{TaskId}' threw an unhandled exception", currentNode.Id);
            var failed = new TaskResult(
                Succeeded: false,
                Messages: new[] { new TaskMessage(
                    MessageSeverity.Error, ex.Message, DateTimeOffset.Now) },
                Duration: sw.Elapsed);
            _results[CurrentIndex] = failed;
            Status = WorkflowSessionStatus.Failed;
            return failed;
        }
    }
}
