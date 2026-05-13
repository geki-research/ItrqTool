using FluentAssertions;
using Xunit;
using ItrqTool.Domain;

namespace ItrqTool.Domain.Tests;

public sealed class WorkflowGraphTests
{
    // TODO: implement when WorkflowGraph.GetTopologicalOrder() is filled in.

    [Fact(Skip = "Placeholder — implement alongside WorkflowGraph")]
    public void SingleNode_ReturnsOneElement() { }

    [Fact(Skip = "Placeholder — implement alongside WorkflowGraph")]
    public void LinearChain_ReturnsCorrectTopologicalOrder() { }

    [Fact(Skip = "Placeholder — implement alongside WorkflowGraph")]
    public void Cycle_ThrowsWorkflowDefinitionException() { }

    [Fact(Skip = "Placeholder — implement alongside WorkflowGraph")]
    public void MissingInputReference_ThrowsWorkflowDefinitionException() { }

    [Fact(Skip = "Placeholder — implement alongside WorkflowGraph")]
    public void DisconnectedMultiRootGraph_ReturnsAllNodes() { }
}
