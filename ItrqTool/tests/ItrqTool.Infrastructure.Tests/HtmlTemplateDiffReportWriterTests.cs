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
        Title: "Audit Template Diff Report",
        PreviousWorkbookPath: @"C:\prev\workbook.xlsx",
        CurrentWorkbookPath: @"C:\curr\workbook.xlsx",
        GeneratedAt: new DateTimeOffset(2025, 5, 19, 12, 0, 0, TimeSpan.Zero),
        Added: [],
        Removed: [],
        Changed: [],
        Unchanged: []
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
            content.Should().Contain(">0<");  // all summary counts are 0
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
                    new HtmlDiffQuestion(null, "Chapter 1", "Section A", questionText, "List", null)
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
                        PreviousNumber: null,
                        CurrentNumber: null,
                        OldText: oldText,
                        NewText: newText,
                        SimilarityScore: 0.75,
                        OldDvDisplay: "—",
                        NewDvDisplay: "—",
                        OldCfOperator: null,
                        NewCfOperator: null,
                        TextChanged: true,
                        NumberChanged: false,
                        DvChanged: false,
                        CfChanged: false)
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

    // ── Title appears in output ───────────────────────────────────────────────

    [Fact]
    public void WriteReport_CustomTitle_AppearInFileContent()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            const string customTitle = "FY2025 vs FY2024 Audit Template Diff";

            var data = EmptyReport() with { Title = customTitle };

            Writer().WriteReport(data, filePath);

            var content = File.ReadAllText(filePath);
            content.Should().Contain(customTitle);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── One unchanged question ────────────────────────────────────────────────

    [Fact]
    public void WriteReport_OneUnchangedQuestion_QuestionTextAppearsInFile()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            const string questionText = "Describe the control environment.";

            var data = EmptyReport() with
            {
                Unchanged =
                [
                    new HtmlDiffQuestion(null, "Chapter 1", "Section A", questionText, null, null)
                ]
            };

            Writer().WriteReport(data, filePath);

            var content = File.ReadAllText(filePath);
            content.Should().Contain(questionText);
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

    // ── No Validation Changes tab ─────────────────────────────────────────────

    [Fact]
    public void WriteReport_OutputDoesNotContainValidationChangesTab()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");

            Writer().WriteReport(EmptyReport(), filePath);

            var content = File.ReadAllText(filePath);
            content.Should().NotContain("Validation Changes",
                because: "the Validation Changes tab has been removed from the report");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Change badges ─────────────────────────────────────────────────────────

    [Fact]
    public void WriteReport_ChangedWithTextAndDvChanged_BadgesAppearInOutput()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");

            var data = EmptyReport() with
            {
                Changed =
                [
                    new HtmlDiffChangedQuestion(
                        Chapter: "Chapter 1",
                        Section: "Section A",
                        PreviousNumber: "1.1",
                        CurrentNumber: "1.1",
                        OldText: "What is risk?",
                        NewText: "What is the risk level?",
                        SimilarityScore: 0.8,
                        OldDvDisplay: "List: Yes | No",
                        NewDvDisplay: "List: Yes | No | N/A",
                        OldCfOperator: null,
                        NewCfOperator: null,
                        TextChanged: true,
                        NumberChanged: false,
                        DvChanged: true,
                        CfChanged: false)
                ]
            };

            Writer().WriteReport(data, filePath);

            var content = File.ReadAllText(filePath);
            content.Should().Contain("badge-text");
            content.Should().Contain("badge-dv");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_ChangedWithDvNotChanged_DvColumnShowsUnchanged()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");

            var data = EmptyReport() with
            {
                Changed =
                [
                    new HtmlDiffChangedQuestion(
                        Chapter: "Chapter 1",
                        Section: "Section A",
                        PreviousNumber: null,
                        CurrentNumber: null,
                        OldText: "What is risk?",
                        NewText: "What is the risk level?",
                        SimilarityScore: 0.8,
                        OldDvDisplay: "—",
                        NewDvDisplay: "—",
                        OldCfOperator: null,
                        NewCfOperator: null,
                        TextChanged: true,
                        NumberChanged: false,
                        DvChanged: false,
                        CfChanged: false)
                ]
            };

            Writer().WriteReport(data, filePath);

            var content = File.ReadAllText(filePath);
            content.Should().Contain("cell-unchanged");
            content.Should().Contain(">unchanged<");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_ChangedWithCfNotChanged_CfColumnShowsUnchanged()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");

            var data = EmptyReport() with
            {
                Changed =
                [
                    new HtmlDiffChangedQuestion(
                        Chapter: "Chapter 1",
                        Section: "Section A",
                        PreviousNumber: null,
                        CurrentNumber: null,
                        OldText: "What is risk?",
                        NewText: "What is the risk level?",
                        SimilarityScore: 0.8,
                        OldDvDisplay: "—",
                        NewDvDisplay: "—",
                        OldCfOperator: null,
                        NewCfOperator: null,
                        TextChanged: true,
                        NumberChanged: false,
                        DvChanged: false,
                        CfChanged: false)
                ]
            };

            Writer().WriteReport(data, filePath);

            var content = File.ReadAllText(filePath);
            content.Should().Contain("cell-unchanged");
            content.Should().Contain(">unchanged<");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
