using System.IO;
using FluentAssertions;
using Xunit;
using ItrqTool.Domain;
using static ItrqTool.Integration.Tests.RlqEDInject.RlqEDWorkbookBuilder;

namespace ItrqTool.Integration.Tests.RlqEDInject;

/// <summary>
/// Wrong-version halts for the RLQ ED inject, with the real reader, writer and structure mediator
/// (mirrors InjectWrongVersionTests). The gate runs before parse, so the workbooks carry only a
/// stamped header row. W3 is hazard H1 at runtime: the STANDARD RLQ inject must reject ED workbooks.
/// </summary>
public sealed class RlqEDInjectWrongVersionTests
{
    // W1 — ED task, previous = standard RLQ v01, current = valid ED v02 → halt on rlq-ed-v01.
    [Fact]
    public async Task EdInject_PreviousIsStandardRlqV01_HaltsWithStructureError()
    {
        var dir = NewWorkDir("w1");
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var outputPath   = Path.Combine(dir, "output.xlsx");
            SaveHeaderOnly(previousPath, "rlq", "v01");
            SaveHeaderOnly(currentPath, "rlq-ed", "v02");

            var result = await EdTask().ExecuteAsync(
                Context(dir, previousPath, currentPath, outputPath, EdInjectConfigPath()), CancellationToken.None);

            result.Succeeded.Should().BeFalse("a standard RLQ v01 workbook is not an ED v01 workbook");
            result.Messages.Should().ContainSingle().Which.Should().Match<TaskMessage>(m =>
                m.Severity == MessageSeverity.Error &&
                m.Text.Contains("does not match the expected rlq-ed-v01 structure"));
            File.Exists(outputPath).Should().BeFalse("the gate halts before anything is written");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // W2 — ED task, previous = valid ED v01, current = standard RLQ v02 → halt on rlq-ed-v02.
    [Fact]
    public async Task EdInject_CurrentIsStandardRlqV02_HaltsWithStructureError()
    {
        var dir = NewWorkDir("w2");
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var outputPath   = Path.Combine(dir, "output.xlsx");
            SaveHeaderOnly(previousPath, "rlq-ed", "v01");
            SaveHeaderOnly(currentPath, "rlq", "v02");

            var result = await EdTask().ExecuteAsync(
                Context(dir, previousPath, currentPath, outputPath, EdInjectConfigPath()), CancellationToken.None);

            result.Succeeded.Should().BeFalse("a standard RLQ v02 template is not an ED v02 template");
            result.Messages.Should().ContainSingle().Which.Should().Match<TaskMessage>(m =>
                m.Severity == MessageSeverity.Error &&
                m.Text.Contains("does not match the expected rlq-ed-v02 structure"));
            File.Exists(outputPath).Should().BeFalse("the gate halts before anything is written");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // W3 (H1) — STANDARD task with valid ED v01 + ED v02 workbooks. StructureGate collects every
    // mismatch, so both inputs are reported: one Error for rlq-v01 and one for rlq-v02.
    [Fact]
    public async Task StandardInject_GivenEdWorkbooks_HaltsOnBothInputs()
    {
        var dir = NewWorkDir("w3");
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var outputPath   = Path.Combine(dir, "output.xlsx");
            SaveHeaderOnly(previousPath, "rlq-ed", "v01");
            SaveHeaderOnly(currentPath, "rlq-ed", "v02");

            var result = await StandardTask().ExecuteAsync(
                Context(dir, previousPath, currentPath, outputPath, StandardInjectConfigPath()), CancellationToken.None);

            result.Succeeded.Should().BeFalse("the standard RLQ inject must reject ED workbooks (hazard H1)");
            result.Messages.Should().HaveCount(2);
            result.Messages.Should().OnlyContain(m => m.Severity == MessageSeverity.Error);
            result.Messages[0].Text.Should().Contain("does not match the expected rlq-v01 structure");
            result.Messages[1].Text.Should().Contain("does not match the expected rlq-v02 structure");
            result.Messages.Should().OnlyContain(m => m.Text.Contains("H2 expected [↓ Answer ↓]"),
                "letter H is where the ED layout first diverges from the standard one");
            File.Exists(outputPath).Should().BeFalse("the gate halts before anything is written");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
