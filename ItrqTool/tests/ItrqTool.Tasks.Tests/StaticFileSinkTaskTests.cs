using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;

namespace ItrqTool.Tasks.Tests;

public sealed class StaticFileSinkTaskTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-static-sink-tests", Guid.NewGuid().ToString("N"));

    private static StaticFileSinkTask MakeTask() => new();

    private static TaskExecutionContext MakeCtx(
        string workDir,
        string? inputPath,
        string? destinationFolder,
        string? destinationFileName)
    {
        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (inputPath is not null)
            inputs["input"] = inputPath;

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (destinationFolder is not null)
            parameters["destinationFolder"] = destinationFolder;
        if (destinationFileName is not null)
            parameters["destinationFileName"] = destinationFileName;

        return new TaskExecutionContext(
            TaskId: "sink",
            InputPaths: inputs,
            OutputPaths: new Dictionary<string, string>(),
            Logger: NullLogger.Instance,
            WorkingDirectory: workDir)
        {
            Parameters = parameters
        };
    }

    // ── happy path: file is copied verbatim to destinationFolder/destinationFileName ──

    [Fact]
    public async Task ExecuteAsync_ValidInput_CopiesFileToDestination()
    {
        var workDir = TestWorkDir();
        var destDir = TestWorkDir();
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(destDir);
        try
        {
            var inputFile = Path.Combine(workDir, "input.xlsx");
            var content = new byte[] { 1, 2, 3, 4, 5 };
            await File.WriteAllBytesAsync(inputFile, content);

            var ctx = MakeCtx(workDir, inputFile, destDir, "final.xlsx");
            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            var dest = Path.Combine(destDir, "final.xlsx");
            File.Exists(dest).Should().BeTrue();
            (await File.ReadAllBytesAsync(dest)).Should().Equal(content);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
            try { Directory.Delete(destDir, recursive: true); } catch (IOException) { }
        }
    }

    // ── creates missing destination folder ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingDestinationFolder_CreatesItAndPlacesFile()
    {
        var workDir = TestWorkDir();
        var destDir = TestWorkDir(); // not created
        Directory.CreateDirectory(workDir);
        try
        {
            var inputFile = Path.Combine(workDir, "data.json");
            await File.WriteAllTextAsync(inputFile, "{\"ok\":true}");

            var ctx = MakeCtx(workDir, inputFile, destDir, "data.json");
            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            Directory.Exists(destDir).Should().BeTrue();
            File.Exists(Path.Combine(destDir, "data.json")).Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
            try { Directory.Delete(destDir, recursive: true); } catch (IOException) { }
        }
    }

    // ── overwrite: existing destination file is replaced ─────────────────────────

    [Fact]
    public async Task ExecuteAsync_ExistingDestinationFile_IsOverwritten()
    {
        var workDir = TestWorkDir();
        var destDir = TestWorkDir();
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(destDir);
        try
        {
            var destFile = Path.Combine(destDir, "out.xlsx");
            await File.WriteAllTextAsync(destFile, "old content");

            var inputFile = Path.Combine(workDir, "new.xlsx");
            await File.WriteAllTextAsync(inputFile, "new content");

            var ctx = MakeCtx(workDir, inputFile, destDir, "out.xlsx");
            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            (await File.ReadAllTextAsync(destFile)).Should().Be("new content");
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
            try { Directory.Delete(destDir, recursive: true); } catch (IOException) { }
        }
    }

    // ── failure: missing input key ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingInputKey_FailsWithError()
    {
        var workDir = TestWorkDir();
        Directory.CreateDirectory(workDir);
        try
        {
            var ctx = MakeCtx(workDir, inputPath: null, destinationFolder: workDir, destinationFileName: "out.xlsx");
            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("input"));
        }
        finally { try { Directory.Delete(workDir, recursive: true); } catch (IOException) { } }
    }

    // ── failure: missing destinationFolder param ──────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingDestinationFolderParam_FailsWithError()
    {
        var workDir = TestWorkDir();
        Directory.CreateDirectory(workDir);
        try
        {
            var inputFile = Path.Combine(workDir, "input.xlsx");
            await File.WriteAllTextAsync(inputFile, "data");

            var ctx = MakeCtx(workDir, inputFile, destinationFolder: null, destinationFileName: "out.xlsx");
            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("destinationFolder"));
        }
        finally { try { Directory.Delete(workDir, recursive: true); } catch (IOException) { } }
    }

    // ── failure: missing destinationFileName param ────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingDestinationFileNameParam_FailsWithError()
    {
        var workDir = TestWorkDir();
        Directory.CreateDirectory(workDir);
        try
        {
            var inputFile = Path.Combine(workDir, "input.xlsx");
            await File.WriteAllTextAsync(inputFile, "data");

            var ctx = MakeCtx(workDir, inputFile, destinationFolder: workDir, destinationFileName: null);
            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("destinationFileName"));
        }
        finally { try { Directory.Delete(workDir, recursive: true); } catch (IOException) { } }
    }

    // ── failure: input file does not exist on disk ────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_InputFileNotFound_FailsWithErrorNamingPath()
    {
        var workDir = TestWorkDir();
        Directory.CreateDirectory(workDir);
        try
        {
            var ghost = Path.Combine(workDir, "ghost.xlsx");

            var ctx = MakeCtx(workDir, ghost, destinationFolder: workDir, destinationFileName: "out.xlsx");
            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains(ghost));
        }
        finally { try { Directory.Delete(workDir, recursive: true); } catch (IOException) { } }
    }

    // ── cancellation propagates ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var workDir = TestWorkDir();
        var destDir = TestWorkDir();
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(destDir);
        try
        {
            var inputFile = Path.Combine(workDir, "input.xlsx");
            await File.WriteAllTextAsync(inputFile, "{}");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var ctx = MakeCtx(workDir, inputFile, destDir, "out.xlsx");
            Func<Task> act = () => MakeTask().ExecuteAsync(ctx, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
            try { Directory.Delete(destDir, recursive: true); } catch (IOException) { }
        }
    }
}
