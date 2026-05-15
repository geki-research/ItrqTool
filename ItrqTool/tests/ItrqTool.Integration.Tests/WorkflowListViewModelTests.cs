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
        var workflow = new WorkflowDefinition("wf1", "Workflow 1", []);
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
