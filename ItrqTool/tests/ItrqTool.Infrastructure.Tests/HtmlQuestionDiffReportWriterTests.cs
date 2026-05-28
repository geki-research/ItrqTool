using FluentAssertions;
using Xunit;
using ItrqTool.Domain.Reporting;
using ItrqTool.Infrastructure.Reporting;

namespace ItrqTool.Infrastructure.Tests;

public sealed class HtmlQuestionDiffReportWriterTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-html-report-tests", Guid.NewGuid().ToString("N"));

    private static HtmlQuestionDiffReportWriter Writer() => new();

    private static HtmlDiffReportData EmptyReport() => new(
        Title: "Question Diff Report",
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
                    new HtmlDiffQuestion(null, "Chapter 1", "Section A", 1, questionText, "List", null)
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
                        PreviousRowNumber: 1,
                        CurrentRowNumber: 1,
                        PreviousNumber: null,
                        CurrentNumber: null,
                        OldText: oldText,
                        NewText: newText,
                        SimilarityScore: 0.75,
                        SecondBestSimilarity: null,
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
                    new HtmlDiffUnchangedQuestion("Chapter 1", "Section A", 1, 1, null, questionText, "—", null, 1.0, null)
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
                        PreviousRowNumber: 1,
                        CurrentRowNumber: 1,
                        PreviousNumber: "1.1",
                        CurrentNumber: "1.1",
                        OldText: "What is risk?",
                        NewText: "What is the risk level?",
                        SimilarityScore: 0.8,
                        SecondBestSimilarity: null,
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
                        PreviousRowNumber: 1,
                        CurrentRowNumber: 1,
                        PreviousNumber: null,
                        CurrentNumber: null,
                        OldText: "What is risk?",
                        NewText: "What is the risk level?",
                        SimilarityScore: 0.8,
                        SecondBestSimilarity: null,
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
                        PreviousRowNumber: 1,
                        CurrentRowNumber: 1,
                        PreviousNumber: null,
                        CurrentNumber: null,
                        OldText: "What is risk?",
                        NewText: "What is the risk level?",
                        SimilarityScore: 0.8,
                        SecondBestSimilarity: null,
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
                    new HtmlDiffUnchangedQuestion("Chapter 2", "Section B", 1, 1, "2.1", questionText, "List: Yes | No", null, 1.0, null)
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

            fnBody.Should().Contain("renderSimCell", because: "renderUnchanged must delegate similarity rendering to renderSimCell");
            content.Should().Contain("sim-green", because: "simClass must return sim-green for high similarity scores");
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

    // ── Phase 2: sim-cell rendering ───────────────────────────────────────────

    [Fact]
    public void WriteReport_RenderSimCell_FunctionBodyContainsRequiredElements()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(EmptyReport(), filePath);
            var content = File.ReadAllText(filePath);

            var fnStart = content.IndexOf("function renderSimCell(", StringComparison.Ordinal);
            fnStart.Should().BeGreaterThan(0, because: "renderSimCell JS function must be present");
            var fnEnd = content.IndexOf("\nfunction ", fnStart + 1, StringComparison.Ordinal);
            if (fnEnd < 0) fnEnd = content.IndexOf("</script>", fnStart, StringComparison.OrdinalIgnoreCase);
            var fnBody = content.Substring(fnStart, fnEnd - fnStart);

            fnBody.Should().Contain("sim-secondary",  because: "secondary score must use sim-secondary class");
            fnBody.Should().Contain("badge-ambiguous", because: "ambiguous margin must show badge-ambiguous");
            fnBody.Should().Contain("0.10",            because: "margin threshold must be 0.10");
            fnBody.Should().Contain("sim-primary",     because: "primary score must use sim-primary class");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_RenderChangedAndUnchanged_DelegateSimilarityToRenderSimCell()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(EmptyReport(), filePath);
            var content = File.ReadAllText(filePath);

            static string GetFnBody(string html, string fnName)
            {
                var start = html.IndexOf("function " + fnName + "(", StringComparison.Ordinal);
                if (start < 0) return string.Empty;
                var end = html.IndexOf("\nfunction ", start + 1, StringComparison.Ordinal);
                if (end < 0) end = html.IndexOf("</script>", start, StringComparison.OrdinalIgnoreCase);
                return html.Substring(start, end - start);
            }

            GetFnBody(content, "renderChanged").Should().Contain("renderSimCell",
                because: "renderChanged must delegate to renderSimCell");
            GetFnBody(content, "renderUnchanged").Should().Contain("renderSimCell",
                because: "renderUnchanged must delegate to renderSimCell");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_ChangedQuestion_SecondBestSimilaritySerializedToJson()
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
                        PreviousRowNumber: 1,
                        CurrentRowNumber: 1,
                        PreviousNumber: null,
                        CurrentNumber: null,
                        OldText: "What is risk?",
                        NewText: "What is risk level?",
                        SimilarityScore: 0.75,
                        SecondBestSimilarity: 0.65,
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
            content.Should().Contain("secondBestSimilarity",
                because: "secondBestSimilarity must be serialized into REPORT_DATA");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_RenderSimCell_NullSecondBestProducesNoSecondarySpan()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(EmptyReport(), filePath);
            var content = File.ReadAllText(filePath);

            var fnStart = content.IndexOf("function renderSimCell(", StringComparison.Ordinal);
            var fnEnd = content.IndexOf("\nfunction ", fnStart + 1, StringComparison.Ordinal);
            if (fnEnd < 0) fnEnd = content.IndexOf("</script>", fnStart, StringComparison.OrdinalIgnoreCase);
            var fnBody = content.Substring(fnStart, fnEnd - fnStart);

            fnBody.Should().Contain("secondBest != null",
                because: "secondary span must be guarded by a null check on secondBest");
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

    // ── Phase E: ExplanationChanged rendering ─────────────────────────────────

    [Fact]
    public void WriteReport_ChangedWithExplanationChangedTrue_RendersExplanationDiffBlock()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            const string oldExplanation = "previous explanation text";
            const string newExplanation = "new explanation text";

            var data = EmptyReport() with
            {
                Changed =
                [
                    new HtmlDiffChangedQuestion(
                        Chapter: "Chapter 1",
                        Section: "Section A",
                        PreviousRowNumber: 1,
                        CurrentRowNumber: 1,
                        PreviousNumber: null,
                        CurrentNumber: null,
                        OldText: "Same question text",
                        NewText: "Same question text",
                        SimilarityScore: 1.0,
                        SecondBestSimilarity: null,
                        OldDvDisplay: "—",
                        NewDvDisplay: "—",
                        OldCfOperator: null,
                        NewCfOperator: null,
                        TextChanged: false,
                        NumberChanged: false,
                        DvChanged: false,
                        CfChanged: false,
                        OldExplanation: oldExplanation,
                        NewExplanation: newExplanation,
                        ExplanationChanged: true)
                ]
            };

            Writer().WriteReport(data, filePath);

            var content = File.ReadAllText(filePath);

            // Explanation text is serialised into the embedded REPORT_DATA JSON.
            content.Should().Contain(oldExplanation,
                because: "old explanation text must be serialized into REPORT_DATA");
            content.Should().Contain(newExplanation,
                because: "new explanation text must be serialized into REPORT_DATA");

            // The renderChanged JS function body must contain the explanation rendering guard
            // and the explanation-block CSS class used to wrap the diff output.
            var fnStart = content.IndexOf("function renderChanged(", StringComparison.Ordinal);
            fnStart.Should().BeGreaterThan(0, because: "renderChanged JS function must be present");
            var fnEnd = content.IndexOf("\nfunction ", fnStart + 1, StringComparison.Ordinal);
            if (fnEnd < 0) fnEnd = content.IndexOf("</script>", fnStart, StringComparison.OrdinalIgnoreCase);
            var fnBody = content.Substring(fnStart, fnEnd - fnStart);

            fnBody.Should().Contain("explanationChanged",
                because: "renderChanged must guard on c.explanationChanged");
            fnBody.Should().Contain("explanation-block",
                because: "renderChanged must reference the explanation-block CSS class");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_ChangedWithExplanationChangedFalse_OmitsExplanationBlock()
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
                        Chapter: "Chapter 1",
                        Section: "Section A",
                        PreviousRowNumber: 1,
                        CurrentRowNumber: 1,
                        PreviousNumber: null,
                        CurrentNumber: null,
                        OldText: oldText,
                        NewText: newText,
                        SimilarityScore: 0.75,
                        SecondBestSimilarity: null,
                        OldDvDisplay: "—",
                        NewDvDisplay: "—",
                        OldCfOperator: null,
                        NewCfOperator: null,
                        TextChanged: true,
                        NumberChanged: false,
                        DvChanged: false,
                        CfChanged: false,
                        OldExplanation: null,
                        NewExplanation: null,
                        ExplanationChanged: false)
                ]
            };

            Writer().WriteReport(data, filePath);

            var content = File.ReadAllText(filePath);

            // The REPORT_DATA JSON must NOT carry explanationChanged:true for this question.
            content.Should().NotContain("\"explanationChanged\":true",
                because: "explanationChanged must be false in the serialized data");

            // Existing question-text rendering must be unaffected — regression guard.
            content.Should().Contain(oldText.Split(' ')[0],
                because: "old question text must still be present in REPORT_DATA");
            content.Should().Contain(newText.Split(' ')[0],
                because: "new question text must still be present in REPORT_DATA");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_LegacyChangedQuestion_RendersWithoutExplanationMarkup()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            const string oldText = "Describe internal controls.";
            const string newText = "Describe the internal control framework.";

            var data = EmptyReport() with
            {
                Changed =
                [
                    new HtmlDiffChangedQuestion(
                        Chapter: "Chapter 2",
                        Section: "Section B",
                        PreviousRowNumber: 1,
                        CurrentRowNumber: 1,
                        PreviousNumber: "2.1",
                        CurrentNumber: "2.1",
                        OldText: oldText,
                        NewText: newText,
                        SimilarityScore: 0.85,
                        SecondBestSimilarity: null,
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

            // Existing question-text rendering must be unaffected — regression guard.
            content.Should().Contain(oldText.Split(' ')[0],
                because: "old question text must appear in legacy-shape output");
            content.Should().Contain(newText.Split(' ')[0],
                because: "new question text must appear in legacy-shape output");

            // ExplanationChanged defaults to false; the JSON must not carry the true flag.
            content.Should().NotContain("\"explanationChanged\":true",
                because: "default ExplanationChanged=false must serialize as false, not true");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Phase F: Explanation diff two-column layout ───────────────────────────

    [Fact]
    public void WriteReport_ChangedWithExplanationChanged_ExplanationBlockAppearsInBothColumns()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            const string oldExplanation = "Explain the previous control approach.";
            const string newExplanation = "Explain the updated control methodology.";

            var data = EmptyReport() with
            {
                Changed =
                [
                    new HtmlDiffChangedQuestion(
                        Chapter: "Chapter 1",
                        Section: "Section A",
                        PreviousRowNumber: 1,
                        CurrentRowNumber: 1,
                        PreviousNumber: null,
                        CurrentNumber: null,
                        OldText: "Same question text",
                        NewText: "Same question text",
                        SimilarityScore: 1.0,
                        SecondBestSimilarity: null,
                        OldDvDisplay: "—",
                        NewDvDisplay: "—",
                        OldCfOperator: null,
                        NewCfOperator: null,
                        TextChanged: false,
                        NumberChanged: false,
                        DvChanged: false,
                        CfChanged: false,
                        OldExplanation: oldExplanation,
                        NewExplanation: newExplanation,
                        ExplanationChanged: true)
                ]
            };

            Writer().WriteReport(data, filePath);

            var content = File.ReadAllText(filePath);

            // The new two-column structure produces two separate expOldBlock / expNewBlock
            // string literals in the JS source, each containing class="explanation-block".
            // Count occurrences — two proves old and new explanation diffs land in separate cells.
            var occurrences = 0;
            var idx = 0;
            while ((idx = content.IndexOf("class=\"explanation-block\"", idx, StringComparison.Ordinal)) >= 0)
            {
                occurrences++;
                idx++;
            }
            occurrences.Should().Be(2,
                because: "old explanation diff block goes in the old-text cell and new explanation diff block goes in the new-text cell");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Phase B: Current sheet / Previous sheet tabs ───────────────────────────

    [Fact]
    public void WriteReport_OutputContainsCurrentSheetTabButton()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(EmptyReport(), filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("onclick=\"showTab('current-sheet')\"",
                because: "the Current sheet tab button must be present");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_OutputContainsPreviousSheetTabButton()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(EmptyReport(), filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("onclick=\"showTab('previous-sheet')\"",
                because: "the Previous sheet tab button must be present");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_OutputContainsCurrentSheetTabPanel()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(EmptyReport(), filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("id=\"tab-current-sheet\"",
                because: "the Current sheet tab panel div must be present");
            content.Should().Contain("id=\"tbody-current-sheet\"",
                because: "the Current sheet tbody must be present");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_OutputContainsPreviousSheetTabPanel()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(EmptyReport(), filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("id=\"tab-previous-sheet\"",
                because: "the Previous sheet tab panel div must be present");
            content.Should().Contain("id=\"tbody-previous-sheet\"",
                because: "the Previous sheet tbody must be present");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_OutputContainsStatusBadgeCssClasses()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(EmptyReport(), filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain(".status-badge-added",
                because: "CSS class for added status badge must be defined");
            content.Should().Contain(".status-badge-removed",
                because: "CSS class for removed status badge must be defined");
            content.Should().Contain(".status-badge-changed",
                because: "CSS class for changed status badge must be defined");
            content.Should().Contain(".status-badge-unchanged",
                because: "CSS class for unchanged status badge must be defined");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_OutputContainsSheetTabJavaScriptFunctions()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(EmptyReport(), filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("function buildSheetEntries(",
                because: "buildSheetEntries JS function must be present");
            content.Should().Contain("function renderSheetTab(",
                because: "renderSheetTab JS function must be present");
            content.Should().Contain("function toggleDetail(",
                because: "toggleDetail JS function must be present");
            content.Should().Contain("function renderEntryDetailCard(",
                because: "renderEntryDetailCard JS function must be present");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_ApplyFilterMapsCurrentAndPreviousSheetTabs()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(EmptyReport(), filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("'current-sheet':",
                because: "applyFilter tbodyId map must include 'current-sheet' key");
            content.Should().Contain("'previous-sheet':",
                because: "applyFilter tbodyId map must include 'previous-sheet' key");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_InitCallsRenderSheetTabForBothSides()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(EmptyReport(), filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("renderSheetTab('current')",
                because: "init block must call renderSheetTab for the current side");
            content.Should().Contain("renderSheetTab('previous')",
                because: "init block must call renderSheetTab for the previous side");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Security / encoding ────────────────────────────────────────────────────

    [Fact]
    public void WriteReport_XssPayloadInQuestionText_IsNotRawInjectedIntoHtml()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var data = EmptyReport() with
            {
                Added =
                [
                    new HtmlDiffQuestion(
                        QuestionNumber: null,
                        Chapter: "Ch1",
                        Section: "Sec1",
                        RowNumber: 1,
                        QuestionText: "<script>alert('PWN')</script>",
                        DvType: null,
                        CfOperator: null)
                ]
            };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            // System.Text.Json HTML-encodes < and > in string values by default,
            // so the raw XSS payload never appears verbatim; the word PWN does.
            content.Should().NotContain("<script>alert('PWN')</script>",
                because: "raw XSS payload must not appear verbatim in the output");
            content.Should().Contain("PWN",
                because: "the question text content must still be present (unicode-escaped)");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
