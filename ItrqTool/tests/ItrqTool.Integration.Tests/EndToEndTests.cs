using System.IO;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation;
using ItrqTool.Presentation.Logging;

namespace ItrqTool.Integration.Tests;

public sealed class EndToEndTests
{
    private static string MakeWorkflowsDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-integration", Guid.NewGuid().ToString("N"));

    private static string MakeWorkflowDataRoot() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-integration-data", Guid.NewGuid().ToString("N"));

    private static void WriteSmokestestJson(string dir) =>
        File.WriteAllText(Path.Combine(dir, "smoketest.json"), """
            {
                "id": "smoketest",
                "name": "Smoke Test Workflow",
                "tasks": [
                    {
                        "id": "first",
                        "type": "NoOp",
                        "inputs": {},
                        "outputs": { "out": "first_output.txt" }
                    },
                    {
                        "id": "second",
                        "type": "NoOp",
                        "inputs": { "in": "first.out" },
                        "outputs": { "out": "second_output.txt" }
                    }
                ]
            }
            """);

    [Fact]
    public async Task FullWorkflowExecution_AllTasksSucceed_AllOutputsWritten()
    {
        var workflowsDir = MakeWorkflowsDir();
        var workflowDataRoot = MakeWorkflowDataRoot();
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        try
        {
            WriteSmokestestJson(workflowsDir);

            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty();
            loadResult.Workflows.Should().HaveCount(1);

            var factory = sp.GetRequiredService<WorkflowSessionFactory>();
            var session = factory.Create(loadResult.Workflows[0]);

            session.WorkingDirectory.Should().Be(Path.Combine(workflowDataRoot, "smoketest"));

            while (session.Status != WorkflowSessionStatus.Completed)
            {
                var result = await session.RunCurrentTaskAsync();
                result.Succeeded.Should().BeTrue();

                if (session.Status != WorkflowSessionStatus.Completed)
                    session.Status.Should().Be(WorkflowSessionStatus.AwaitingReview);
            }

            session.Status.Should().Be(WorkflowSessionStatus.Completed);
            session.CurrentIndex.Should().Be(2);

            File.Exists(Path.Combine(session.WorkingDirectory, "first_output.txt")).Should().BeTrue();
            File.Exists(Path.Combine(session.WorkingDirectory, "second_output.txt")).Should().BeTrue();
            new FileInfo(Path.Combine(session.WorkingDirectory, "first_output.txt")).Length.Should().Be(0);
            new FileInfo(Path.Combine(session.WorkingDirectory, "second_output.txt")).Length.Should().Be(0);

            session.GetResult(0).Should().NotBeNull();
            session.GetResult(0)!.Succeeded.Should().BeTrue();
            session.GetResult(1).Should().NotBeNull();
            session.GetResult(1)!.Succeeded.Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(workflowsDir, recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task RunningTask_PushesLogEntriesToSink()
    {
        var workflowsDir = MakeWorkflowsDir();
        var workflowDataRoot = MakeWorkflowDataRoot();
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        try
        {
            WriteSmokestestJson(workflowsDir);

            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            var factory = sp.GetRequiredService<WorkflowSessionFactory>();
            var session = factory.Create(loadResult.Workflows[0]);

            var result = await session.RunCurrentTaskAsync();
            result.Succeeded.Should().BeTrue();

            var sink = sp.GetRequiredService<IUiLogSink>();
            sink.Entries.Count.Should().BeGreaterThanOrEqualTo(2);
            sink.Entries.Should().Contain(e => e.ShortCategory == "NoOpTask");
        }
        finally
        {
            try { Directory.Delete(workflowsDir, recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void ScrutorDiscovery_AllTasksResolvableViaRegistry()
    {
        var workflowsDir = MakeWorkflowsDir();
        var workflowDataRoot = MakeWorkflowDataRoot();
        Directory.CreateDirectory(workflowsDir);

        try
        {
            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var tasks = sp.GetServices<IWorkflowTask>().ToList();
            tasks.Should().NotBeEmpty();

            var registry = sp.GetRequiredService<ITaskRegistry>();

            foreach (var task in tasks)
            {
                var found = registry.FindTask(task.TaskType);
                found.Should().NotBeNull(because: $"task type '{task.TaskType}' should be resolvable via the registry");
                found!.TaskType.Should().Be(task.TaskType);
            }
        }
        finally
        {
            try { Directory.Delete(workflowsDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task SecondSessionRun_WipesPreExistingFiles()
    {
        var workflowsDir = MakeWorkflowsDir();
        var workflowDataRoot = MakeWorkflowDataRoot();
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        try
        {
            WriteSmokestestJson(workflowsDir);

            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            var definition = loadResult.Workflows[0];

            var factory = sp.GetRequiredService<WorkflowSessionFactory>();

            // First session: run to completion.
            var firstSession = factory.Create(definition);
            var workingDir = firstSession.WorkingDirectory;
            workingDir.Should().Be(Path.Combine(workflowDataRoot, "smoketest"));

            while (firstSession.Status != WorkflowSessionStatus.Completed)
                await firstSession.RunCurrentTaskAsync();

            File.Exists(Path.Combine(workingDir, "first_output.txt")).Should().BeTrue();
            File.Exists(Path.Combine(workingDir, "second_output.txt")).Should().BeTrue();

            // Write a stale file simulating old run output.
            File.WriteAllText(Path.Combine(workingDir, "stale.txt"), "stale");

            // Second session: fresh session for the same workflow.
            var secondSession = factory.Create(definition);
            secondSession.WorkingDirectory.Should().Be(workingDir);

            await secondSession.RunCurrentTaskAsync();

            File.Exists(Path.Combine(workingDir, "stale.txt")).Should().BeFalse();
            File.Exists(Path.Combine(workingDir, "first_output.txt")).Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(workflowsDir, recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
        }
    }
}
