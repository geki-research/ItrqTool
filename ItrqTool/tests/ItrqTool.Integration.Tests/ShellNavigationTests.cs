using System.IO;
using FluentAssertions;
using NSubstitute;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation.Logging;
using ItrqTool.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace ItrqTool.Integration.Tests;

public sealed class ShellNavigationTests
{
    private static WorkflowListViewModel MakeListVm(IWorkflowLoader loader)
        => new(loader);

    private static WorkflowRunViewModel MakeRunVm()
    {
        var workflowDataRoot = Path.Combine(Path.GetTempPath(), "ItrqTool-nav-tests", Guid.NewGuid().ToString("N"));
        var factory = new WorkflowSessionFactory(
            workflowDataRoot,
            Substitute.For<ITaskRegistry>(),
            NullLogger<WorkflowSession>.Instance);
        return new WorkflowRunViewModel(factory, new UiLogSink(null));
    }

    [Fact]
    public void Shell_StartsOnListView()
    {
        var loader = Substitute.For<IWorkflowLoader>();
        loader.LoadAll().Returns(new WorkflowLoadResult([], []));

        var listVm = MakeListVm(loader);
        var runVm = MakeRunVm();
        var shell = new ShellViewModel(listVm, runVm);

        shell.CurrentViewModel.Should().BeSameAs(listVm);
    }

    [Fact]
    public void Shell_NavigatesToRunView_WhenWorkflowSelected()
    {
        var definition = new WorkflowDefinition("test-wf", "Test Workflow", []);
        var loader = Substitute.For<IWorkflowLoader>();
        loader.LoadAll().Returns(new WorkflowLoadResult([definition], []));

        var listVm = MakeListVm(loader);
        var runVm = MakeRunVm();
        var shell = new ShellViewModel(listVm, runVm);

        listVm.SelectedWorkflow = listVm.Workflows[0];
        listVm.SelectCurrentCommand.Execute(null);

        shell.CurrentViewModel.Should().BeSameAs(runVm);
        runVm.WorkflowName.Should().Be("Test Workflow");
    }

    [Fact]
    public void Shell_NavigatesBackToList_WhenBackRequested()
    {
        var definition = new WorkflowDefinition("test-wf", "Test Workflow", []);
        var loader = Substitute.For<IWorkflowLoader>();
        loader.LoadAll().Returns(new WorkflowLoadResult([definition], []));

        var listVm = MakeListVm(loader);
        var runVm = MakeRunVm();
        var shell = new ShellViewModel(listVm, runVm);

        // Navigate forward
        listVm.SelectedWorkflow = listVm.Workflows[0];
        listVm.SelectCurrentCommand.Execute(null);
        shell.CurrentViewModel.Should().BeSameAs(runVm);

        // Navigate back
        runVm.BackCommand.Execute(null);

        shell.CurrentViewModel.Should().BeSameAs(listVm);
        loader.Received(2).LoadAll();
    }
}
