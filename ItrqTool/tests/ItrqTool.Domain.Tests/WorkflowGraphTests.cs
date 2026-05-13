using FluentAssertions;
using Xunit;
using ItrqTool.Domain;

namespace ItrqTool.Domain.Tests;

public sealed class WorkflowGraphTests
{
    [Fact]
    public void SingleNode_ReturnsOneElement()
    {
        var node = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef>(),
            new Dictionary<string, string> { ["out"] = "a.csv" });
        var def = new WorkflowDefinition("wf", "WF", [node]);

        var order = new WorkflowGraph(def).GetTopologicalOrder();

        order.Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public void LinearChain_ReturnsCorrectTopologicalOrder()
    {
        var a = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef>(),
            new Dictionary<string, string> { ["out"] = "a.csv" });
        var b = new TaskNode("b", "TypeB",
            new Dictionary<string, TaskOutputRef> { ["in"] = new TaskOutputRef("a", "out") },
            new Dictionary<string, string> { ["out"] = "b.csv" });
        var c = new TaskNode("c", "TypeC",
            new Dictionary<string, TaskOutputRef> { ["in"] = new TaskOutputRef("b", "out") },
            new Dictionary<string, string> { ["out"] = "c.csv" });
        var def = new WorkflowDefinition("wf", "WF", [a, b, c]);

        var order = new WorkflowGraph(def).GetTopologicalOrder();

        order.Select(n => n.Id).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Cycle_ThrowsWorkflowDefinitionException()
    {
        var a = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef> { ["in"] = new TaskOutputRef("b", "out") },
            new Dictionary<string, string> { ["out"] = "a.csv" });
        var b = new TaskNode("b", "TypeB",
            new Dictionary<string, TaskOutputRef> { ["in"] = new TaskOutputRef("a", "out") },
            new Dictionary<string, string> { ["out"] = "b.csv" });
        var def = new WorkflowDefinition("wf", "WF", [a, b]);

        var act = () => new WorkflowGraph(def);

        act.Should().ThrowExactly<WorkflowDefinitionException>();
    }

    [Fact]
    public void MissingUpstreamTaskId_ThrowsWorkflowDefinitionException()
    {
        var node = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef> { ["in"] = new TaskOutputRef("ghost", "out") },
            new Dictionary<string, string>());
        var def = new WorkflowDefinition("wf", "WF", [node]);

        var act = () => new WorkflowGraph(def);

        act.Should().ThrowExactly<WorkflowDefinitionException>()
            .WithMessage("*unknown upstream task*");
    }

    [Fact]
    public void MissingUpstreamOutputKey_ThrowsWorkflowDefinitionException()
    {
        var a = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef>(),
            new Dictionary<string, string> { ["out"] = "a.csv" });
        var b = new TaskNode("b", "TypeB",
            new Dictionary<string, TaskOutputRef> { ["in"] = new TaskOutputRef("a", "missing") },
            new Dictionary<string, string>());
        var def = new WorkflowDefinition("wf", "WF", [a, b]);

        var act = () => new WorkflowGraph(def);

        act.Should().ThrowExactly<WorkflowDefinitionException>()
            .WithMessage("*unknown output key*");
    }

    [Fact]
    public void DisconnectedMultiRootGraph_ReturnsAllNodes()
    {
        var a = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef>(),
            new Dictionary<string, string> { ["out"] = "a.csv" });
        var b = new TaskNode("b", "TypeB",
            new Dictionary<string, TaskOutputRef> { ["in"] = new TaskOutputRef("a", "out") },
            new Dictionary<string, string>());
        var c = new TaskNode("c", "TypeC",
            new Dictionary<string, TaskOutputRef>(),
            new Dictionary<string, string> { ["out"] = "c.csv" });
        var d = new TaskNode("d", "TypeD",
            new Dictionary<string, TaskOutputRef> { ["in"] = new TaskOutputRef("c", "out") },
            new Dictionary<string, string>());
        var def = new WorkflowDefinition("wf", "WF", [a, b, c, d]);

        var ids = new WorkflowGraph(def).GetTopologicalOrder().Select(n => n.Id).ToList();

        ids.Should().HaveCount(4).And.Contain(["a", "b", "c", "d"]);
        ids.IndexOf("a").Should().BeLessThan(ids.IndexOf("b"));
        ids.IndexOf("c").Should().BeLessThan(ids.IndexOf("d"));
    }
}
