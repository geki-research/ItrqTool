using FluentAssertions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Presentation.ViewModels;

namespace ItrqTool.Integration.Tests;

public sealed class WorkflowListViewModelTests
{
    private static WorkflowListViewModel MakeVm(IWorkflowLoader loader) => new(loader);

    private static IWorkflowLoader LoaderWith(
        IReadOnlyList<WorkflowDefinition> workflows,
        IReadOnlyList<WorkflowLoadFailure> failures)
    {
        var loader = Substitute.For<IWorkflowLoader>();
        loader.LoadAll().Returns(new WorkflowLoadResult(workflows, failures));
        return loader;
    }

    [Fact]
    public void Load_WhenNoFailures_HasFailuresIsFalse()
    {
        var workflow = new WorkflowDefinition("wf1", "Workflow 1", null, []);
        var loader = LoaderWith([workflow], []);
        var vm = MakeVm(loader);

        vm.Load();

        vm.HasFailures.Should().BeFalse();
        vm.Failures.Should().BeEmpty();
        vm.FailureSummary.Should().BeEmpty();
    }

    [Fact]
    public void Load_WhenOneFailure_PopulatesBannerSingular()
    {
        var failure = new WorkflowLoadFailure(@"C:\some\path\bad.json", "Unexpected token");
        var loader = LoaderWith([], [failure]);
        var vm = MakeVm(loader);

        vm.Load();

        vm.HasFailures.Should().BeTrue();
        vm.Failures.Should().HaveCount(1);
        vm.Failures[0].FileName.Should().Be("bad.json");
        vm.Failures[0].ErrorMessage.Should().Be("Unexpected token");
        vm.FailureSummary.Should().Be("1 workflow file failed to load.");
        vm.ShowFailureDetails.Should().BeFalse();
    }

    [Fact]
    public void Load_WhenMultipleFailures_PopulatesBannerPlural()
    {
        var failures = new[]
        {
            new WorkflowLoadFailure(@"C:\path\one.json", "Syntax error"),
            new WorkflowLoadFailure(@"C:\path\two.json", "Missing field")
        };
        var loader = LoaderWith([], failures);
        var vm = MakeVm(loader);

        vm.Load();

        vm.FailureSummary.Should().Be("2 workflow files failed to load.");
    }

    [Fact]
    public void ToggleFailureDetails_FlipsVisibility()
    {
        var failure = new WorkflowLoadFailure(@"C:\some\path\bad.json", "Unexpected token");
        var loader = LoaderWith([], [failure]);
        var vm = MakeVm(loader);
        vm.Load();

        vm.ShowFailureDetails.Should().BeFalse();

        vm.ToggleFailureDetailsCommand.Execute(null);
        vm.ShowFailureDetails.Should().BeTrue();

        vm.ToggleFailureDetailsCommand.Execute(null);
        vm.ShowFailureDetails.Should().BeFalse();
    }

    // ── Grouping ───────────────────────────────────────────────────────────────

    [Fact]
    public void Load_TwoWorkflowsInSameGroup_ProduceOneGroupWithTwoLeaves()
    {
        var wf1 = new WorkflowDefinition("wf1", "Alpha", "Audit 2025", []);
        var wf2 = new WorkflowDefinition("wf2", "Beta", "Audit 2025", []);
        var loader = LoaderWith([wf1, wf2], []);
        var vm = MakeVm(loader);

        vm.Load();

        vm.WorkflowGroups.Should().HaveCount(1);
        vm.WorkflowGroups[0].GroupName.Should().Be("Audit 2025");
        vm.WorkflowGroups[0].Workflows.Should().HaveCount(2);
    }

    [Fact]
    public void Load_OneGroupedOneUngrouped_UngroupedIsLast()
    {
        var wf1 = new WorkflowDefinition("wf1", "Named Workflow", "Audit", []);
        var wf2 = new WorkflowDefinition("wf2", "Bare Workflow", null, []);
        var loader = LoaderWith([wf1, wf2], []);
        var vm = MakeVm(loader);

        vm.Load();

        vm.WorkflowGroups.Should().HaveCount(2);
        vm.WorkflowGroups[0].GroupName.Should().Be("Audit");
        vm.WorkflowGroups[1].GroupName.Should().Be("Ungrouped");
    }

    [Fact]
    public void Load_NullGroup_AppearsUnderUngrouped()
    {
        var wf = new WorkflowDefinition("wf1", "Some Workflow", null, []);
        var loader = LoaderWith([wf], []);
        var vm = MakeVm(loader);

        vm.Load();

        vm.WorkflowGroups.Should().HaveCount(1);
        vm.WorkflowGroups[0].GroupName.Should().Be("Ungrouped");
        vm.WorkflowGroups[0].Workflows.Should().HaveCount(1);
    }

    [Fact]
    public void Load_SelectedWorkflowIsNull_AfterLoad()
    {
        var wf = new WorkflowDefinition("wf1", "Workflow 1", "Group A", []);
        var loader = LoaderWith([wf], []);
        var vm = MakeVm(loader);

        vm.Load();

        vm.SelectedWorkflow.Should().BeNull();
    }

    [Fact]
    public void SelectCurrentCommand_CanExecuteIsFalse_WhenNoSelection()
    {
        var wf = new WorkflowDefinition("wf1", "Workflow 1", "Group A", []);
        var loader = LoaderWith([wf], []);
        var vm = MakeVm(loader);
        vm.Load();

        vm.SelectCurrentCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SelectCurrentCommand_CanExecuteIsTrue_WhenWorkflowSelected()
    {
        var wf = new WorkflowDefinition("wf1", "Workflow 1", "Group A", []);
        var loader = LoaderWith([wf], []);
        var vm = MakeVm(loader);
        vm.Load();
        vm.SelectedWorkflow = vm.WorkflowGroups[0].Workflows[0];

        vm.SelectCurrentCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Load_ResetsFailureDetailsToCollapsed()
    {
        var failure = new WorkflowLoadFailure(@"C:\some\path\bad.json", "Unexpected token");
        var loader = LoaderWith([], [failure]);
        var vm = MakeVm(loader);

        vm.Load();
        vm.ShowFailureDetails = true;

        vm.Load();

        vm.ShowFailureDetails.Should().BeFalse();
    }
}
