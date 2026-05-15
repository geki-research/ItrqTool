using System.IO;
using CommunityToolkit.Mvvm.Input;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation.UIModels;
using ItrqTool.Presentation.ViewModels;

namespace ItrqTool.Integration.Tests;

public sealed class WorkflowRunViewModelTests
{
    private static string MakeWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-vmtests", Guid.NewGuid().ToString("N"));

    private static WorkflowRunViewModel MakeVm(ITaskRegistry registry, string workDir)
    {
        var factory = new WorkflowSessionFactory(
            workDir,
            registry,
            NullLogger<WorkflowSession>.Instance);
        return new WorkflowRunViewModel(factory);
    }

    private static IWorkflowTask NoOpTask(string taskType)
    {
        var task = Substitute.For<IWorkflowTask>();
        task.TaskType.Returns(taskType);
        task.ExecuteAsync(Arg.Any<TaskExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new TaskResult(
                Succeeded: true,
                Messages: new[] { new TaskMessage(MessageSeverity.Info, "ok", DateTimeOffset.Now) },
                Duration: TimeSpan.FromMilliseconds(10)));
        return task;
    }

    private static IWorkflowTask FailingTask(string taskType)
    {
        var task = Substitute.For<IWorkflowTask>();
        task.TaskType.Returns(taskType);
        task.ExecuteAsync(Arg.Any<TaskExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new TaskResult(
                Succeeded: false,
                Messages: new[] { new TaskMessage(MessageSeverity.Error, "fail", DateTimeOffset.Now) },
                Duration: TimeSpan.FromMilliseconds(5)));
        return task;
    }

    [Fact]
    public void InitializeFor_PopulatesRows_FromTopologicalOrder()
    {
        var workDir = MakeWorkDir();
        try
        {
            var nodeA = new TaskNode("taskA", "TypeA",
                new Dictionary<string, TaskOutputRef>(),
                new Dictionary<string, string> { ["out"] = "a.txt" });
            var nodeB = new TaskNode("taskB", "TypeB",
                new Dictionary<string, TaskOutputRef> { ["in"] = new TaskOutputRef("taskA", "out") },
                new Dictionary<string, string>());
            var def = new WorkflowDefinition("wf", "Test Workflow", [nodeA, nodeB]);

            var taskA = NoOpTask("TypeA");
            var taskB = NoOpTask("TypeB");
            var registry = Substitute.For<ITaskRegistry>();
            registry.FindTask("TypeA").Returns(taskA);
            registry.FindTask("TypeB").Returns(taskB);

            var vm = MakeVm(registry, workDir);
            vm.InitializeFor(def);

            vm.Tasks.Should().HaveCount(2);
            vm.Tasks[0].Status.Should().Be(TaskRowStatus.Ready);
            vm.Tasks[1].Status.Should().Be(TaskRowStatus.Pending);
            vm.RunButtonLabel.Should().Be("Run first task");
            vm.CanRun.Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void InitializeFor_EmptyWorkflow_DisablesRun()
    {
        var workDir = MakeWorkDir();
        try
        {
            var def = new WorkflowDefinition("wf", "Empty Workflow", []);
            var registry = Substitute.For<ITaskRegistry>();
            var vm = MakeVm(registry, workDir);

            vm.InitializeFor(def);

            vm.Tasks.Should().HaveCount(0);
            vm.RunButtonLabel.Should().Be("No tasks to run");
            vm.CanRun.Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task RunTask_SuccessFlow_AdvancesRowsAndLabels()
    {
        var workDir = MakeWorkDir();
        try
        {
            var nodeA = new TaskNode("taskA", "TypeA",
                new Dictionary<string, TaskOutputRef>(),
                new Dictionary<string, string> { ["out"] = "a.txt" });
            var nodeB = new TaskNode("taskB", "TypeB",
                new Dictionary<string, TaskOutputRef> { ["in"] = new TaskOutputRef("taskA", "out") },
                new Dictionary<string, string> { ["out"] = "b.txt" });
            var def = new WorkflowDefinition("wf", "Test Workflow", [nodeA, nodeB]);

            var taskA = NoOpTask("TypeA");
            var taskB = NoOpTask("TypeB");
            var registry = Substitute.For<ITaskRegistry>();
            registry.FindTask("TypeA").Returns(taskA);
            registry.FindTask("TypeB").Returns(taskB);

            var vm = MakeVm(registry, workDir);
            vm.InitializeFor(def);

            await vm.RunTaskCommand.ExecuteAsync(null);

            vm.Tasks[0].Status.Should().Be(TaskRowStatus.Completed);
            vm.Tasks[0].Duration.Should().NotBeNull();
            vm.Tasks[1].Status.Should().Be(TaskRowStatus.Ready);
            vm.SelectedResult.Should().NotBeNull();
            vm.SelectedResult!.Succeeded.Should().BeTrue();
            vm.SelectedTask.Should().BeSameAs(vm.Tasks[0]);
            vm.RunButtonLabel.Should().Be("Run next task");

            await vm.RunTaskCommand.ExecuteAsync(null);

            vm.Tasks[1].Status.Should().Be(TaskRowStatus.Completed);
            vm.RunButtonLabel.Should().Be("Workflow completed");
            vm.CanRun.Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task RunTask_Failure_MarksRowFailed_StopsExecution()
    {
        var workDir = MakeWorkDir();
        try
        {
            var node = new TaskNode("taskA", "TypeA",
                new Dictionary<string, TaskOutputRef>(),
                new Dictionary<string, string>());
            var def = new WorkflowDefinition("wf", "Test Workflow", [node]);

            var failTask = FailingTask("TypeA");
            var registry = Substitute.For<ITaskRegistry>();
            registry.FindTask("TypeA").Returns(failTask);

            var vm = MakeVm(registry, workDir);
            vm.InitializeFor(def);

            await vm.RunTaskCommand.ExecuteAsync(null);

            vm.Tasks[0].Status.Should().Be(TaskRowStatus.Failed);
            vm.SelectedResult.Should().NotBeNull();
            vm.SelectedResult!.Succeeded.Should().BeFalse();
            vm.SelectedResult.Messages.Should().NotBeEmpty();
            vm.SelectedResult.Messages.Should().Contain(m => m.Severity == TaskMessageSeverity.Error);
            vm.CanRun.Should().BeFalse();
            vm.RunButtonLabel.Should().Be("Workflow failed");
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task SelectedTask_ChangesResult_ToHistoricalResult()
    {
        var workDir = MakeWorkDir();
        try
        {
            var nodeA = new TaskNode("taskA", "TypeA",
                new Dictionary<string, TaskOutputRef>(),
                new Dictionary<string, string> { ["out"] = "a.txt" });
            var nodeB = new TaskNode("taskB", "TypeB",
                new Dictionary<string, TaskOutputRef> { ["in"] = new TaskOutputRef("taskA", "out") },
                new Dictionary<string, string> { ["out"] = "b.txt" });
            var def = new WorkflowDefinition("wf", "Test Workflow", [nodeA, nodeB]);

            var taskA = NoOpTask("TypeA");
            var taskB = NoOpTask("TypeB");
            var registry = Substitute.For<ITaskRegistry>();
            registry.FindTask("TypeA").Returns(taskA);
            registry.FindTask("TypeB").Returns(taskB);

            var vm = MakeVm(registry, workDir);
            vm.InitializeFor(def);

            await vm.RunTaskCommand.ExecuteAsync(null);
            await vm.RunTaskCommand.ExecuteAsync(null);

            vm.SelectedTask = vm.Tasks[0];
            vm.SelectedResult.Should().NotBeNull();
            vm.SelectedResult!.TaskName.Should().Be("taskA");

            vm.SelectedTask = vm.Tasks[1];
            vm.SelectedResult.Should().NotBeNull();
            vm.SelectedResult!.TaskName.Should().Be("taskB");
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void OpenWorkingFolderCommand_IsDisabled_BeforeInitialize()
    {
        var workDir = MakeWorkDir();
        var registry = Substitute.For<ITaskRegistry>();
        var vm = MakeVm(registry, workDir);

        vm.OpenWorkingFolderCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void OpenWorkingFolderCommand_IsEnabled_AfterInitialize()
    {
        var workDir = MakeWorkDir();
        try
        {
            var node = new TaskNode("taskA", "TypeA",
                new Dictionary<string, TaskOutputRef>(),
                new Dictionary<string, string> { ["out"] = "a.txt" });
            var def = new WorkflowDefinition("wf", "Test Workflow", [node]);

            var taskA = NoOpTask("TypeA");
            var registry = Substitute.For<ITaskRegistry>();
            registry.FindTask("TypeA").Returns(taskA);

            var vm = MakeVm(registry, workDir);
            vm.InitializeFor(def);

            vm.OpenWorkingFolderCommand.CanExecute(null).Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void OpenWorkingFolderCommand_CreatesDirectory_IfMissing()
    {
        // Trade-off: this test also launches explorer.exe as a side-effect.
        // We do NOT attempt to close the window — enumerating
        // Process.GetProcessesByName("explorer") and killing processes
        // would close ALL of the user's open Explorer windows.
        // On a developer machine a single folder window will briefly appear;
        // on CI (headless) Process.Start may fail silently, which is caught
        // and swallowed by the command's try/catch — the directory assertion
        // still holds in both environments.
        var workflowDataRoot = MakeWorkDir();
        try
        {
            var node = new TaskNode("taskA", "TypeA",
                new Dictionary<string, TaskOutputRef>(),
                new Dictionary<string, string> { ["out"] = "a.txt" });
            var def = new WorkflowDefinition("open-folder-wf", "Test Workflow", [node]);

            var taskA = NoOpTask("TypeA");
            var registry = Substitute.For<ITaskRegistry>();
            registry.FindTask("TypeA").Returns(taskA);

            var vm = MakeVm(registry, workflowDataRoot);
            vm.InitializeFor(def);

            var expectedPath = Path.Combine(workflowDataRoot, def.Id);
            Directory.Exists(expectedPath).Should().BeFalse("working directory is created lazily on first task run");

            vm.OpenWorkingFolderCommand.Execute(null);

            Directory.Exists(expectedPath).Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
        }
    }
}
