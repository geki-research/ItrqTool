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
                    new HtmlDiffUnchangedQuestion("Chapter 1", "Section A", null, questionText, "—", null)
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

    // ── Unchanged tab structure ───────────────────────────────────────────────

    [Fact]
    public void WriteReport_OneUnchangedQuestion_RowContainsExpectedStructure()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            const string questionText = "Describe the governance framework.";

            var data = EmptyReport() with
            {
                Unchanged =
                [
                    new HtmlDiffUnchangedQuestion("Chapter 2", "Section B", "2.1", questionText, "List: Yes | No", null)
                ]
            };

            Writer().WriteReport(data, filePath);

            var content = File.ReadAllText(filePath);

            // Question text is serialised into the embedded REPORT_DATA JSON
            content.Should().Contain(questionText);

            // Extract the renderUnchanged JS function body — rows are JS-rendered at runtime,
            // so the function source code is the right place to verify rendering logic
            var fnStart = content.IndexOf("function renderUnchanged()", StringComparison.Ordinal);
            fnStart.Should().BeGreaterThan(0, because: "renderUnchanged JS function must be present");
            var fnEnd = content.IndexOf("\nfunction ", fnStart + 1, StringComparison.Ordinal);
            if (fnEnd < 0) fnEnd = content.IndexOf("</script>", fnStart, StringComparison.OrdinalIgnoreCase);
            var fnBody = content.Substring(fnStart, fnEnd - fnStart);

            fnBody.Should().Contain("sim-green", because: "similarity cell must use sim-green class");
            fnBody.Should().Contain("100%",       because: "similarity must be rendered as 100%");
            fnBody.Should().Contain("cell-unchanged");

            var unchangedSpanCount = 0;
            var idx = 0;
            while ((idx = fnBody.IndexOf(">unchanged<", idx, StringComparison.Ordinal)) >= 0) { unchangedSpanCount++; idx++; }
            unchangedSpanCount.Should().BeGreaterThanOrEqualTo(2, because: "DV and CF columns both show 'unchanged'");

            fnBody.Should().NotContain("badge-text", because: "unchanged rows never emit change badges");
            fnBody.Should().NotContain("badge-dv");
            fnBody.Should().NotContain("badge-cf");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_UnchangedTableHasSameColumnCountAsChangedTable()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");

            Writer().WriteReport(EmptyReport(), filePath);

            var content = File.ReadAllText(filePath);

            static int CountThInThead(string html, string tbodyId)
            {
                // Find the <thead> that immediately precedes the table body identified by tbodyId
                var tbodyPos = html.IndexOf($"id=\"{tbodyId}\"", StringComparison.Ordinal);
                if (tbodyPos < 0) return -1;
                var theadEnd = html.LastIndexOf("</thead>", tbodyPos, StringComparison.OrdinalIgnoreCase);
                if (theadEnd < 0) return -1;
                var theadStart = html.LastIndexOf("<thead>", theadEnd, StringComparison.OrdinalIgnoreCase);
                if (theadStart < 0) return -1;
                var theadHtml = html.Substring(theadStart, theadEnd - theadStart);
                var count = 0;
                var idx = 0;
                while ((idx = theadHtml.IndexOf("<th>", idx, StringComparison.OrdinalIgnoreCase)) >= 0) { count++; idx++; }
                return count;
            }

            var changedCols   = CountThInThead(content, "tbody-changed");
            var unchangedCols = CountThInThead(content, "tbody-unchanged");

            changedCols.Should().BePositive(because: "changed table must have a thead");
            unchangedCols.Should().Be(changedCols, because: "unchanged table must mirror changed table column count");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
