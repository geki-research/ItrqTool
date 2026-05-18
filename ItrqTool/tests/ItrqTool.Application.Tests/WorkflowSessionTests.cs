using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;

namespace ItrqTool.Application.Tests;

public sealed class WorkflowSessionTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static TaskResult SuccessResult() => new(
        Succeeded: true,
        Messages: new[] { new TaskMessage(MessageSeverity.Info, "ok", DateTimeOffset.Now) },
        Duration: TimeSpan.FromMilliseconds(1));

    private static TaskResult FailResult() => new(
        Succeeded: false,
        Messages: new[] { new TaskMessage(MessageSeverity.Error, "fail", DateTimeOffset.Now) },
        Duration: TimeSpan.FromMilliseconds(1));

    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-tests", Guid.NewGuid().ToString("N"));

    // ── tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public void InitialStatus_IsReadyToRun()
    {
        var node = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef>(),
            new Dictionary<string, string> { ["out"] = "a.txt" });
        var def = new WorkflowDefinition("wf", "WF", [node]);
        var workDir = TestWorkDir();

        var session = new WorkflowSession(
            def, Substitute.For<ITaskRegistry>(), workDir,
            NullLogger<WorkflowSession>.Instance);

        session.Status.Should().Be(WorkflowSessionStatus.ReadyToRun);
        session.CurrentIndex.Should().Be(0);
    }

    [Fact]
    public async Task RunCurrentTask_SetsStatusToRunning_ThenAwaitingReview()
    {
        var a = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef>(),
            new Dictionary<string, string> { ["out"] = "a.txt" });
        var b = new TaskNode("b", "TypeB",
            new Dictionary<string, TaskOutputRef> { ["in"] = new TaskOutputRef("a", "out") },
            new Dictionary<string, string>());
        var def = new WorkflowDefinition("wf", "WF", [a, b]);
        var workDir = TestWorkDir();

        WorkflowSession? session = null;
        WorkflowSessionStatus? observedStatus = null;

        var taskA = Substitute.For<IWorkflowTask>();
        taskA.ExecuteAsync(Arg.Any<TaskExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                observedStatus = session!.Status;
                return Task.FromResult(SuccessResult());
            });

        var registry = Substitute.For<ITaskRegistry>();
        registry.FindTask("TypeA").Returns(taskA);

        session = new WorkflowSession(def, registry, workDir, NullLogger<WorkflowSession>.Instance);

        try
        {
            await session.RunCurrentTaskAsync();

            observedStatus.Should().Be(WorkflowSessionStatus.Running);
            session.Status.Should().Be(WorkflowSessionStatus.AwaitingReview);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task RunCurrentTask_SetsStatusToFailed_WhenTaskReturnsFailed()
    {
        var node = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef>(),
            new Dictionary<string, string>());
        var def = new WorkflowDefinition("wf", "WF", [node]);
        var workDir = TestWorkDir();

        var taskA = Substitute.For<IWorkflowTask>();
        taskA.ExecuteAsync(Arg.Any<TaskExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(FailResult()));

        var registry = Substitute.For<ITaskRegistry>();
        registry.FindTask("TypeA").Returns(taskA);

        var session = new WorkflowSession(def, registry, workDir, NullLogger<WorkflowSession>.Instance);

        try
        {
            var result = await session.RunCurrentTaskAsync();

            session.Status.Should().Be(WorkflowSessionStatus.Failed);
            session.CurrentIndex.Should().Be(0);
            session.GetResult(0).Should().BeSameAs(result);
            result.Succeeded.Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task RunCurrentTask_SetsStatusToCompleted_AfterLastTask()
    {
        var a = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef>(),
            new Dictionary<string, string> { ["out"] = "a.txt" });
        var b = new TaskNode("b", "TypeB",
            new Dictionary<string, TaskOutputRef> { ["in"] = new TaskOutputRef("a", "out") },
            new Dictionary<string, string>());
        var def = new WorkflowDefinition("wf", "WF", [a, b]);
        var workDir = TestWorkDir();

        var taskA = Substitute.For<IWorkflowTask>();
        taskA.ExecuteAsync(Arg.Any<TaskExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessResult()));

        var taskB = Substitute.For<IWorkflowTask>();
        taskB.ExecuteAsync(Arg.Any<TaskExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessResult()));

        var registry = Substitute.For<ITaskRegistry>();
        registry.FindTask("TypeA").Returns(taskA);
        registry.FindTask("TypeB").Returns(taskB);

        var session = new WorkflowSession(def, registry, workDir, NullLogger<WorkflowSession>.Instance);

        try
        {
            await session.RunCurrentTaskAsync();
            session.Status.Should().Be(WorkflowSessionStatus.AwaitingReview);
            session.CurrentIndex.Should().Be(1);

            await session.RunCurrentTaskAsync();
            session.Status.Should().Be(WorkflowSessionStatus.Completed);
            session.CurrentIndex.Should().Be(2);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task RunCurrentTask_PropagatesInputPaths_FromUpstreamOutputs()
    {
        var a = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef>(),
            new Dictionary<string, string> { ["data"] = "a_out.csv" });
        var b = new TaskNode("b", "TypeB",
            new Dictionary<string, TaskOutputRef> { ["src"] = new TaskOutputRef("a", "data") },
            new Dictionary<string, string> { ["result"] = "b_out.csv" });
        var def = new WorkflowDefinition("wf", "WF", [a, b]);
        var workDir = TestWorkDir();

        TaskExecutionContext? capturedContext = null;

        var taskA = Substitute.For<IWorkflowTask>();
        taskA.ExecuteAsync(Arg.Any<TaskExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessResult()));

        var taskB = Substitute.For<IWorkflowTask>();
        taskB.ExecuteAsync(Arg.Any<TaskExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedContext = ci.ArgAt<TaskExecutionContext>(0);
                return Task.FromResult(SuccessResult());
            });

        var registry = Substitute.For<ITaskRegistry>();
        registry.FindTask("TypeA").Returns(taskA);
        registry.FindTask("TypeB").Returns(taskB);

        var session = new WorkflowSession(def, registry, workDir, NullLogger<WorkflowSession>.Instance);

        try
        {
            await session.RunCurrentTaskAsync();
            await session.RunCurrentTaskAsync();

            capturedContext.Should().NotBeNull();
            capturedContext!.InputPaths["src"].Should().Be(
                Path.Combine(workDir, "a_out.csv"));
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task RunCurrentTask_PlacesOutputs_InWorkingDirectory()
    {
        var node = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef>(),
            new Dictionary<string, string> { ["result"] = "out.txt" });
        var def = new WorkflowDefinition("wf", "WF", [node]);
        var workDir = TestWorkDir();

        TaskExecutionContext? capturedContext = null;

        var taskA = Substitute.For<IWorkflowTask>();
        taskA.ExecuteAsync(Arg.Any<TaskExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedContext = ci.ArgAt<TaskExecutionContext>(0);
                return Task.FromResult(SuccessResult());
            });

        var registry = Substitute.For<ITaskRegistry>();
        registry.FindTask("TypeA").Returns(taskA);

        var session = new WorkflowSession(def, registry, workDir, NullLogger<WorkflowSession>.Instance);

        try
        {
            await session.RunCurrentTaskAsync();

            capturedContext.Should().NotBeNull();
            capturedContext!.OutputPaths["result"].Should().Be(Path.Combine(workDir, "out.txt"));
            Directory.Exists(workDir).Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task RunCurrentTask_WipesWorkingDirectory_OnFirstExecution()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "ItrqTool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        File.WriteAllText(Path.Combine(workDir, "stale.txt"), "stale content");

        var node = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef>(),
            new Dictionary<string, string> { ["out"] = "fresh.txt" });
        var def = new WorkflowDefinition("wf", "WF", [node]);

        var taskA = Substitute.For<IWorkflowTask>();
        taskA.ExecuteAsync(Arg.Any<TaskExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var ctx = ci.ArgAt<TaskExecutionContext>(0);
                File.WriteAllText(ctx.OutputPaths["out"], "fresh");
                return Task.FromResult(SuccessResult());
            });

        var registry = Substitute.For<ITaskRegistry>();
        registry.FindTask("TypeA").Returns(taskA);

        var session = new WorkflowSession(def, registry, workDir, NullLogger<WorkflowSession>.Instance);

        try
        {
            await session.RunCurrentTaskAsync();

            File.Exists(Path.Combine(workDir, "stale.txt")).Should().BeFalse();
            File.Exists(Path.Combine(workDir, "fresh.txt")).Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task RunCurrentTask_PassesNodeParameters_ToContext()
    {
        var nodeParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["key"] = "value" };
        var node = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef>(),
            new Dictionary<string, string>())
        {
            Parameters = nodeParameters
        };
        var def = new WorkflowDefinition("wf", "WF", [node]);
        var workDir = TestWorkDir();

        TaskExecutionContext? capturedContext = null;

        var taskA = Substitute.For<IWorkflowTask>();
        taskA.ExecuteAsync(Arg.Any<TaskExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedContext = ci.ArgAt<TaskExecutionContext>(0);
                return Task.FromResult(SuccessResult());
            });

        var registry = Substitute.For<ITaskRegistry>();
        registry.FindTask("TypeA").Returns(taskA);

        var session = new WorkflowSession(def, registry, workDir, NullLogger<WorkflowSession>.Instance);

        try
        {
            await session.RunCurrentTaskAsync();

            capturedContext.Should().NotBeNull();
            capturedContext!.WorkingDirectory.Should().Be(workDir);
            capturedContext.Parameters["key"].Should().Be("value");
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task RunCurrentTask_PropagatesCancellation()
    {
        var node = new TaskNode("a", "TypeA",
            new Dictionary<string, TaskOutputRef>(),
            new Dictionary<string, string>());
        var def = new WorkflowDefinition("wf", "WF", [node]);
        var workDir = TestWorkDir();

        var taskA = Substitute.For<IWorkflowTask>();
        taskA.ExecuteAsync(Arg.Any<TaskExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<TaskResult>(new CancellationToken(canceled: true)));

        var registry = Substitute.For<ITaskRegistry>();
        registry.FindTask("TypeA").Returns(taskA);

        var session = new WorkflowSession(def, registry, workDir, NullLogger<WorkflowSession>.Instance);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var act = async () => await session.RunCurrentTaskAsync(cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            session.Status.Should().Be(WorkflowSessionStatus.ReadyToRun);
            session.CurrentIndex.Should().Be(0);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }
}
