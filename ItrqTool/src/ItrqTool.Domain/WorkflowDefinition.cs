namespace ItrqTool.Domain;

public record WorkflowDefinition(
    string HierarchicalPath,
    string Name,
    string? Group,
    IReadOnlyList<TaskNode> Nodes
)
{
    public IReadOnlyList<string> IdentitySegments => [.. HierarchicalPath.Split(':'), Name];

    public string IdentityKey => string.Join('/', IdentitySegments);
}
