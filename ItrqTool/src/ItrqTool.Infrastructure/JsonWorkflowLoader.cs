using System.Text.Json;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;

namespace ItrqTool.Infrastructure;

public sealed class JsonWorkflowLoader : IWorkflowLoader
{
    private readonly string _workflowsDirectoryPath;
    private readonly ILogger<JsonWorkflowLoader> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public JsonWorkflowLoader(string workflowsDirectoryPath, ILogger<JsonWorkflowLoader> logger)
    {
        ArgumentNullException.ThrowIfNull(workflowsDirectoryPath);
        ArgumentNullException.ThrowIfNull(logger);
        if (string.IsNullOrWhiteSpace(workflowsDirectoryPath))
            throw new ArgumentException(
                "Path cannot be empty or whitespace.", nameof(workflowsDirectoryPath));

        _workflowsDirectoryPath = workflowsDirectoryPath;
        _logger = logger;
    }

    public WorkflowLoadResult LoadAll()
    {
        if (!Directory.Exists(_workflowsDirectoryPath))
            throw new DirectoryNotFoundException(
                $"Workflow directory not found: '{_workflowsDirectoryPath}'.");

        var workflows = new List<WorkflowDefinition>();
        var failures = new List<WorkflowLoadFailure>();

        var files = Directory
            .GetFiles(_workflowsDirectoryPath, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();

        foreach (var filePath in files)
        {
            try
            {
                var definition = ParseFile(filePath);
                _ = new WorkflowGraph(definition);
                workflows.Add(definition);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load workflow from '{FilePath}'.", filePath);
                failures.Add(new WorkflowLoadFailure(filePath, ex.Message));
            }
        }

        return new WorkflowLoadResult(workflows, failures);
    }

    private static WorkflowDefinition ParseFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var dto = JsonSerializer.Deserialize<WorkflowDto>(json, JsonOptions)
            ?? throw new ArgumentException("JSON deserialized to null.");

        if (string.IsNullOrEmpty(dto.Id))
            throw new ArgumentException("Workflow 'id' is required.");
        if (string.IsNullOrEmpty(dto.Name))
            throw new ArgumentException("Workflow 'name' is required.");
        if (dto.Tasks is null)
            throw new ArgumentException("Workflow 'tasks' is required.");

        var nodes = new List<TaskNode>(dto.Tasks.Count);
        foreach (var taskDto in dto.Tasks)
        {
            if (string.IsNullOrEmpty(taskDto.Id))
                throw new ArgumentException("Task 'id' is required.");
            if (string.IsNullOrEmpty(taskDto.Type))
                throw new ArgumentException($"Task '{taskDto.Id}' 'type' is required.");
            if (taskDto.Inputs is null)
                throw new ArgumentException($"Task '{taskDto.Id}' 'inputs' is required.");
            if (taskDto.Outputs is null)
                throw new ArgumentException($"Task '{taskDto.Id}' 'outputs' is required.");

            var inputs = new Dictionary<string, TaskOutputRef>();
            foreach (var (localKey, reference) in taskDto.Inputs)
            {
                var dot = reference.IndexOf('.');
                if (dot <= 0 || dot == reference.Length - 1)
                    throw new ArgumentException(
                        $"Malformed input reference '{reference}' for key '{localKey}': " +
                        "expected 'taskId.outputKey'.");
                inputs[localKey] = new TaskOutputRef(reference[..dot], reference[(dot + 1)..]);
            }

            IReadOnlyDictionary<string, string> parameters = taskDto.Parameters is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : taskDto.Parameters.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);

            nodes.Add(new TaskNode(
                taskDto.Id,
                taskDto.Type,
                inputs,
                new Dictionary<string, string>(taskDto.Outputs))
            {
                Parameters = parameters
            });
        }

        return new WorkflowDefinition(dto.Id, dto.Name, dto.Group, nodes);
    }

    // ── Private DTOs — not part of the public API ──────────────────────────────
    // Explicit public constructors are required so System.Text.Json can
    // instantiate these types via reflection.

    private sealed class WorkflowDto
    {
        public WorkflowDto() { }
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Group { get; set; }
        public List<TaskDto>? Tasks { get; set; }
    }

    private sealed class TaskDto
    {
        public TaskDto() { }
        public string? Id { get; set; }
        public string? Type { get; set; }
        public Dictionary<string, string>? Inputs { get; set; }
        public Dictionary<string, string>? Outputs { get; set; }
        public Dictionary<string, string?>? Parameters { get; set; }
    }
}
