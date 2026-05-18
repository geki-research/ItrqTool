namespace ItrqTool.Domain;

public record WorkflowDefinition(
    string Id,
    string Name,
    string? Group,
    IReadOnlyList<TaskNode> Nodes
);
