using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Tasks;

namespace ItrqTool.Tasks.Tests;

public sealed class NoOpTaskTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-tasks-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteAsync_WritesEmptyFile_ForEachOutputPath()
    {
        var workDir = TestWorkDir();
        Directory.CreateDirectory(workDir);

        try
        {
            var out1 = Path.Combine(workDir, "output1.txt");
            var out2 = Path.Combine(workDir, "output2.txt");

            var context = new TaskExecutionContext(
                TaskId: "test",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["a"] = out1, ["b"] = out2 },
                Logger: NullLogger.Instance,
                WorkingDirectory: workDir);

            var result = await new NoOpTask().ExecuteAsync(context, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            File.Exists(out1).Should().BeTrue();
            File.Exists(out2).Should().BeTrue();
            new FileInfo(out1).Length.Should().Be(0);
            new FileInfo(out2).Length.Should().Be(0);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenOutputDirectoryDoesNotExist()
    {
        var workDir = TestWorkDir();
        Directory.CreateDirectory(workDir);

        try
        {
            // Create a FILE where a directory is expected so File.WriteAllTextAsync fails.
            var blockingFile = Path.Combine(workDir, "subdir");
            await File.WriteAllTextAsync(blockingFile, "blocking");

            var context = new TaskExecutionContext(
                TaskId: "test",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string>
                {
                    ["result"] = Path.Combine(workDir, "subdir", "out.txt")
                },
                Logger: NullLogger.Instance,
                WorkingDirectory: workDir);

            var result = await new NoOpTask().ExecuteAsync(context, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellation()
    {
        var workDir = TestWorkDir();
        Directory.CreateDirectory(workDir);

        try
        {
            var context = new TaskExecutionContext(
                TaskId: "test",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string>
                {
                    ["result"] = Path.Combine(workDir, "out.txt")
                },
                Logger: NullLogger.Instance,
                WorkingDirectory: workDir);

            var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = async () => await new NoOpTask().ExecuteAsync(context, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
        }
    }
}
