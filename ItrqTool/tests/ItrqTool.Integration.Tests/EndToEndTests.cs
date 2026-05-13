using System.IO;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation;

namespace ItrqTool.Integration.Tests;

public sealed class EndToEndTests
{
    private static string MakeWorkflowsDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-integration", Guid.NewGuid().ToString("N"));

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
        Directory.CreateDirectory(workflowsDir);
        string? sessionWorkingDir = null;

        try
        {
            WriteSmokestestJson(workflowsDir);

            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir);
            using var sp = services.BuildServiceProvider();

            var loader = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty();
            loadResult.Workflows.Should().HaveCount(1);

            var factory = sp.GetRequiredService<WorkflowSessionFactory>();
            var session = factory.Create(loadResult.Workflows[0]);
            sessionWorkingDir = session.WorkingDirectory;

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
            if (sessionWorkingDir is not null)
                try { Directory.Delete(sessionWorkingDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void ScrutorDiscovery_AllTasksResolvableViaRegistry()
    {
        var workflowsDir = MakeWorkflowsDir();
        Directory.CreateDirectory(workflowsDir);

        try
        {
            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir);
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
}
