using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;

namespace ItrqTool.Application.Tests;

public sealed class WorkflowSessionFactoryTests
{
    private static WorkflowSessionFactory MakeFactory(string root) =>
        new(root, Substitute.For<ITaskRegistry>(), NullLogger<WorkflowSession>.Instance);

    [Fact]
    public void Create_FlatId_WorkingDirectoryIsRootPlusId()
    {
        var root = Path.Combine(Path.GetTempPath(), "ItrqTool-factory-tests");
        var factory = MakeFactory(root);
        var def = new WorkflowDefinition("myworkflow", "My Workflow", null, []);

        var session = factory.Create(def);

        session.WorkingDirectory.Should().Be(Path.Combine(root, "myworkflow"));
    }

    [Fact]
    public void Create_HierarchicalId_WorkingDirectoryHasNestedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "ItrqTool-factory-tests");
        var factory = MakeFactory(root);
        var def = new WorkflowDefinition("A:B:task", "Task", null, []);

        var session = factory.Create(def);

        session.WorkingDirectory.Should().Be(Path.Combine(root, "A", "B", "task"));
    }

    [Fact]
    public void Create_TwoSegmentId_WorkingDirectoryHasOneNestingLevel()
    {
        var root = Path.Combine(Path.GetTempPath(), "ItrqTool-factory-tests");
        var factory = MakeFactory(root);
        var def = new WorkflowDefinition("Audit:checklist", "Checklist", null, []);

        var session = factory.Create(def);

        session.WorkingDirectory.Should().Be(Path.Combine(root, "Audit", "checklist"));
    }
}
