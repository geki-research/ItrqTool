using FluentAssertions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Presentation.UIModels;
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
        vm.WorkflowGroups[0].Children.OfType<WorkflowListItem>().Should().HaveCount(2);
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
        vm.WorkflowGroups[0].Children.OfType<WorkflowListItem>().Should().HaveCount(1);
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
        vm.SelectedWorkflow = vm.WorkflowGroups[0].Children.OfType<WorkflowListItem>().First();

        vm.SelectCurrentCommand.CanExecute(null).Should().BeTrue();
    }

    // ── Hierarchical grouping ──────────────────────────────────────────────────

    [Fact]
    public void Load_GroupWithColon_ProducesNestedGroupNodes()
    {
        var wf = new WorkflowDefinition("wf1", "Task", "Year:Audit", []);
        var loader = LoaderWith([wf], []);
        var vm = MakeVm(loader);

        vm.Load();

        vm.WorkflowGroups.Should().HaveCount(1);
        var root = vm.WorkflowGroups[0];
        root.GroupName.Should().Be("Year");
        var sub = root.Children.OfType<WorkflowGroupItem>().Single();
        sub.GroupName.Should().Be("Audit");
        sub.Children.OfType<WorkflowListItem>().Should().HaveCount(1);
    }

    [Fact]
    public void Load_HierarchicalIdWithDerivedGroup_ProducesNestedNodes()
    {
        // id="A:B:task" → derived group "A:B" → A > B > leaf
        var wf = new WorkflowDefinition("A:B:task", "Task", "A:B", []);
        var loader = LoaderWith([wf], []);
        var vm = MakeVm(loader);

        vm.Load();

        var root = vm.WorkflowGroups.Single();
        root.GroupName.Should().Be("A");
        var sub = root.Children.OfType<WorkflowGroupItem>().Single();
        sub.GroupName.Should().Be("B");
        sub.Children.OfType<WorkflowListItem>().Single().Name.Should().Be("Task");
    }

    [Fact]
    public void Load_GroupWithSpacesInSegments_ProducesFullTwoLevelTree()
    {
        // Regression: "ITRQ RefYear 2025:Stage 0" must produce A → B → leaf, not A → leaf
        var wf = new WorkflowDefinition("wf1", "Audit Task", "ITRQ RefYear 2025:Stage 0", []);
        var loader = LoaderWith([wf], []);
        var vm = MakeVm(loader);

        vm.Load();

        vm.WorkflowGroups.Should().HaveCount(1);
        var root = vm.WorkflowGroups[0];
        root.GroupName.Should().Be("ITRQ RefYear 2025");
        var sub = root.Children.OfType<WorkflowGroupItem>().Single();
        sub.GroupName.Should().Be("Stage 0");
        sub.Children.OfType<WorkflowListItem>().Single().Name.Should().Be("Audit Task");
    }

    [Fact]
    public void Load_ThreeLevelGroup_ProducesFullThreeLevelTree()
    {
        // "A:B:C" must produce A → B → C → leaf (not collapsed or missing intermediate levels)
        var wf = new WorkflowDefinition("wf1", "Deep Task", "A:B:C", []);
        var loader = LoaderWith([wf], []);
        var vm = MakeVm(loader);

        vm.Load();

        var root = vm.WorkflowGroups.Single();
        root.GroupName.Should().Be("A");
        var level2 = root.Children.OfType<WorkflowGroupItem>().Single();
        level2.GroupName.Should().Be("B");
        var level3 = level2.Children.OfType<WorkflowGroupItem>().Single();
        level3.GroupName.Should().Be("C");
        level3.Children.OfType<WorkflowListItem>().Single().Name.Should().Be("Deep Task");
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
