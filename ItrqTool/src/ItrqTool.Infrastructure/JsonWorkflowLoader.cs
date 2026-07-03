using System.Text.Json;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;

namespace ItrqTool.Infrastructure;

public sealed class JsonWorkflowLoader : IWorkflowLoader
{
    // Deepest session artifact under a workflow's working directory is a flat
    // file; longest in-repo output name is "control-level-question-diff.html"
    // (32 chars), so 60 is safe headroom for a child file name plus separator.
    private const int MaxPath = 259;
    private const int ChildBudget = 60;

    private readonly string _workflowsDirectoryPath;
    private readonly string _workflowDataRoot;
    private readonly ILogger<JsonWorkflowLoader> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public JsonWorkflowLoader(
        string workflowsDirectoryPath,
        string workflowDataRoot,
        ILogger<JsonWorkflowLoader> logger)
    {
        ArgumentNullException.ThrowIfNull(workflowsDirectoryPath);
        ArgumentNullException.ThrowIfNull(workflowDataRoot);
        ArgumentNullException.ThrowIfNull(logger);
        if (string.IsNullOrWhiteSpace(workflowsDirectoryPath))
            throw new ArgumentException(
                "Path cannot be empty or whitespace.", nameof(workflowsDirectoryPath));
        if (string.IsNullOrWhiteSpace(workflowDataRoot))
            throw new ArgumentException(
                "Path cannot be empty or whitespace.", nameof(workflowDataRoot));

        _workflowsDirectoryPath = workflowsDirectoryPath;
        _workflowDataRoot = workflowDataRoot;
        _logger = logger;
    }

    public WorkflowLoadResult LoadAll()
    {
        if (!Directory.Exists(_workflowsDirectoryPath))
            throw new DirectoryNotFoundException(
                $"Workflow directory not found: '{_workflowsDirectoryPath}'.");

        var workflows = new List<WorkflowDefinition>();
        var failures = new List<WorkflowLoadFailure>();
        var seenIdentities = new HashSet<string>(StringComparer.Ordinal);

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
                ValidateResolvedPathLength(definition, _workflowDataRoot);

                if (!seenIdentities.Add(definition.IdentityKey))
                {
                    failures.Add(new WorkflowLoadFailure(
                        filePath,
                        $"Duplicate workflow identity '{definition.HierarchicalPath}' / '{definition.Name}' " +
                        "(already defined by an earlier file); each (hierarchicalPath, name) must be unique."));
                    continue;
                }

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

        if (string.IsNullOrEmpty(dto.HierarchicalPath))
        {
            if (!string.IsNullOrEmpty(dto.Id))
                throw new ArgumentException(
                    "Workflow field 'id' was renamed to 'hierarchicalPath'; please update this file.");
            throw new ArgumentException("Workflow 'hierarchicalPath' is required.");
        }
        ValidateIdSegments(dto.HierarchicalPath);
        if (string.IsNullOrEmpty(dto.Name))
            throw new ArgumentException("Workflow 'name' is required.");
        ValidateNameSegment(dto.Name);
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

        var effectiveGroup = dto.Group ?? DeriveGroup(dto.HierarchicalPath);
        return new WorkflowDefinition(dto.HierarchicalPath, dto.Name, effectiveGroup, nodes);
    }

    // Windows-illegal filename characters (superset of the original '/','\','.' check).
    private static readonly char[] IllegalPathChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // Returns null when the segment is filesystem-legal; otherwise a human-readable reason.
    private static string? DescribeSegmentViolation(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return "is an empty segment";

        foreach (var c in segment)
        {
            if (c < ' ')
                return "contains a control character";
            if (Array.IndexOf(IllegalPathChars, c) >= 0)
                return $"contains an illegal path character ('{c}')";
        }

        if (segment[^1] is '.' or ' ')
            return "ends with a trailing '.' or space";

        var dotIndex = segment.IndexOf('.');
        var baseName = dotIndex >= 0 ? segment[..dotIndex] : segment;
        if (ReservedDeviceNames.Contains(baseName))
            return $"is a reserved device name ('{baseName}')";

        return null;
    }

    private static void ValidateIdSegments(string id)
    {
        foreach (var segment in id.Split(':'))
        {
            var violation = DescribeSegmentViolation(segment);
            if (violation is not null)
                throw new ArgumentException(
                    $"Workflow hierarchicalPath '{id}' segment '{segment}' {violation}.");
        }
    }

    private static void ValidateNameSegment(string name)
    {
        var violation = DescribeSegmentViolation(name);
        if (violation is not null)
            throw new ArgumentException($"Workflow name '{name}' {violation}.");
    }

    private static void ValidateResolvedPathLength(WorkflowDefinition definition, string workflowDataRoot)
    {
        var joined = string.Join('\\', definition.IdentitySegments);
        var workingDirectoryPathLength = workflowDataRoot.Length + 1 + joined.Length + 1;
        if (workingDirectoryPathLength + ChildBudget > MaxPath)
            throw new ArgumentException(
                $"Resolved working-directory path is too long ({workingDirectoryPathLength} chars, " +
                $"limit {MaxPath - ChildBudget}); shorten the hierarchicalPath/name.");
    }

    private static string? DeriveGroup(string id)
        => id.Contains(':') ? id : null;

    // ── Private DTOs — not part of the public API ──────────────────────────────
    // Explicit public constructors are required so System.Text.Json can
    // instantiate these types via reflection.

    private sealed class WorkflowDto
    {
        public WorkflowDto() { }
        public string? HierarchicalPath { get; set; }

        // Legacy field name, retained only to detect un-migrated files and
        // surface a targeted rename message instead of a generic "required" error.
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
