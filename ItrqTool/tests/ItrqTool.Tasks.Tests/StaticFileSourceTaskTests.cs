using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;

namespace ItrqTool.Tasks.Tests;

public sealed class StaticFileSourceTaskTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-static-source-tests", Guid.NewGuid().ToString("N"));

    private static StaticFileSourceTask MakeTask() => new();

    private static TaskExecutionContext MakeCtx(
        string dir,
        string? sourcePath,
        string outputFile = "out.json")
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (sourcePath is not null)
            parameters["sourcePath"] = sourcePath;

        return new TaskExecutionContext(
            TaskId: "copy",
            InputPaths: new Dictionary<string, string>(),
            OutputPaths: new Dictionary<string, string> { ["output"] = Path.Combine(dir, outputFile) },
            Logger: NullLogger.Instance,
            WorkingDirectory: dir)
        {
            Parameters = parameters
        };
    }

    // ── success: file is copied verbatim to the output path ──────────────────

    [Fact]
    public async Task ExecuteAsync_ValidSourcePath_CopiesFileToOutput()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "source.json");
            const string content = "{\"test\": 1}";
            await File.WriteAllTextAsync(source, content);

            var ctx = MakeCtx(dir, source, "output.json");
            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            var outputPath = ctx.OutputPaths["output"];
            File.Exists(outputPath).Should().BeTrue();
            (await File.ReadAllTextAsync(outputPath)).Should().Be(content);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── failure: missing sourcePath parameter ─────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingSourcePathParam_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var ctx = MakeCtx(dir, sourcePath: null);
            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("sourcePath"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── failure: source file does not exist on disk ───────────────────────────

    [Fact]
    public async Task ExecuteAsync_SourceFileNotFound_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var ctx = MakeCtx(dir, Path.Combine(dir, "nonexistent.json"));
            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── cancellation propagates ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "source.json");
            await File.WriteAllTextAsync(source, "{}");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var ctx = MakeCtx(dir, source);
            Func<Task> act = () => MakeTask().ExecuteAsync(ctx, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
