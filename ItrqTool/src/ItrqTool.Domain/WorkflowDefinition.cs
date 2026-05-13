namespace ItrqTool.Domain;

public record WorkflowDefinition(
    string Id,
    string Name,
    IReadOnlyList<TaskNode> Nodes
);
