namespace ItrqTool.Domain;

// Builds and validates the DAG for a workflow; provides topological ordering.
// Throws WorkflowDefinitionException on cycles or missing input references.
public sealed class WorkflowGraph
{
    private readonly WorkflowDefinition _definition;

    public WorkflowGraph(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
    }

    public IReadOnlyList<TaskNode> GetTopologicalOrder() => throw new NotImplementedException();
}
