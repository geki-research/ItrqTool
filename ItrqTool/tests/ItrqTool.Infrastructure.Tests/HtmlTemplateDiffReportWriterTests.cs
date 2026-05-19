using FluentAssertions;
using Xunit;
using ItrqTool.Domain.Reporting;
using ItrqTool.Infrastructure.Reporting;

namespace ItrqTool.Infrastructure.Tests;

public sealed class HtmlTemplateDiffReportWriterTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-html-report-tests", Guid.NewGuid().ToString("N"));

    private static HtmlTemplateDiffReportWriter Writer() => new();

    private static HtmlDiffReportData EmptyReport() => new(
        PreviousWorkbookPath: @"C:\prev\workbook.xlsx",
        CurrentWorkbookPath: @"C:\curr\workbook.xlsx",
        GeneratedAt: new DateTimeOffset(2025, 5, 19, 12, 0, 0, TimeSpan.Zero),
        Added: [],
        Removed: [],
        Changed: [],
        ValidationChanges: []
    );

    // ── Empty report ───────────────────────────────────────────────────────────

    [Fact]
    public void WriteReport_EmptyData_CreatesFileContainingHtmlAndZeroCounts()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");

            Writer().WriteReport(EmptyReport(), filePath);

            File.Exists(filePath).Should().BeTrue();
            var content = File.ReadAllText(filePath);
            content.Should().Contain("<html");
            content.Should().Contain(">0<");  // all four summary counts are 0
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── One added question ─────────────────────────────────────────────────────

    [Fact]
    public void WriteReport_OneAddedQuestion_QuestionTextAppearsInFile()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            const string questionText = "What is the residual risk appetite?";

            var data = EmptyReport() with
            {
                Added =
                [
                    new HtmlDiffQuestion("Chapter 1", "Section A", questionText, "List", null)
                ]
            };

            Writer().WriteReport(data, filePath);

            var content = File.ReadAllText(filePath);
            content.Should().Contain(questionText);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── One changed question ───────────────────────────────────────────────────

    [Fact]
    public void WriteReport_OneChangedQuestion_BothOldAndNewTextAppearInFile()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            const string oldText = "What is risk tolerance?";
            const string newText = "What is the risk tolerance threshold?";

            var data = EmptyReport() with
            {
                Changed =
                [
                    new HtmlDiffChangedQuestion(
                        Chapter: "Chapter 2",
                        Section: "Section B",
                        OldText: oldText,
                        NewText: newText,
                        SimilarityScore: 0.75,
                        DvTypeChanged: false,
                        CfOperatorChanged: false)
                ]
            };

            Writer().WriteReport(data, filePath);

            var content = File.ReadAllText(filePath);
            content.Should().Contain(oldText.Split(' ')[0]); // at least first word of old text
            content.Should().Contain(newText.Split(' ')[0]); // at least first word of new text
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Overwrite ──────────────────────────────────────────────────────────────

    [Fact]
    public void WriteReport_OverwritesExistingFile_WithoutThrowing()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");

            Writer().WriteReport(EmptyReport(), filePath);
            var act = () => Writer().WriteReport(EmptyReport(), filePath);

            act.Should().NotThrow();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── No external resource references ───────────────────────────────────────

    [Fact]
    public void WriteReport_OutputContainsNoExternalResourceReferences()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");

            Writer().WriteReport(EmptyReport(), filePath);

            var content = File.ReadAllText(filePath);
            content.Should().NotContain("http://",   because: "the report must be fully self-contained");
            content.Should().NotContain("https://",  because: "the report must be fully self-contained");
            content.Should().NotContain("<link ",    because: "no external stylesheets allowed");
            content.Should().NotContain("src=\"http", because: "no external script or image src");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
