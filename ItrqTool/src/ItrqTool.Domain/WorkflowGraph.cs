namespace ItrqTool.Domain;

public sealed class WorkflowGraph
{
    private readonly IReadOnlyList<TaskNode> _topologicalOrder;

    public WorkflowGraph(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _topologicalOrder = BuildAndValidate(definition);
    }

    public IReadOnlyList<TaskNode> GetTopologicalOrder() => _topologicalOrder;

    private static IReadOnlyList<TaskNode> BuildAndValidate(WorkflowDefinition definition)
    {
        var nodeById = definition.Nodes.ToDictionary(n => n.Id);

        // a. Missing-input validation
        foreach (var node in definition.Nodes)
        {
            foreach (var (inputKey, outputRef) in node.Inputs)
            {
                if (!nodeById.TryGetValue(outputRef.TaskId, out var upstream))
                    throw new WorkflowDefinitionException(
                        $"Task '{node.Id}' input '{inputKey}' references unknown upstream task '{outputRef.TaskId}'.");

                if (!upstream.OutputFileNames.ContainsKey(outputRef.OutputKey))
                    throw new WorkflowDefinitionException(
                        $"Task '{node.Id}' input '{inputKey}' references unknown output key '{outputRef.OutputKey}' on upstream task '{outputRef.TaskId}'.");
            }
        }

        // b. Kahn's algorithm — cycle detection and topological order
        var inDegree = definition.Nodes.ToDictionary(n => n.Id, _ => 0);
        var downstream = definition.Nodes.ToDictionary(n => n.Id, _ => new List<string>());

        foreach (var node in definition.Nodes)
        {
            foreach (var upstreamId in node.Inputs.Values.Select(r => r.TaskId).Distinct())
            {
                inDegree[node.Id]++;
                downstream[upstreamId].Add(node.Id);
            }
        }

        var queue = new Queue<string>(
            definition.Nodes.Where(n => inDegree[n.Id] == 0).Select(n => n.Id));

        var order = new List<TaskNode>(definition.Nodes.Count);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            order.Add(nodeById[id]);
            foreach (var downId in downstream[id])
            {
                if (--inDegree[downId] == 0)
                    queue.Enqueue(downId);
            }
        }

        if (order.Count != definition.Nodes.Count)
        {
            var cycleNode = definition.Nodes.First(n => inDegree[n.Id] > 0);
            throw new WorkflowDefinitionException(
                $"Workflow contains a cycle involving node '{cycleNode.Id}'.");
        }

        return order;
    }
}
