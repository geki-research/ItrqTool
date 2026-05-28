using FluentAssertions;
using Xunit;
using ItrqTool.Domain.Reporting;
using ItrqTool.Infrastructure.Reporting;

namespace ItrqTool.Infrastructure.Tests;

public sealed class HtmlGeneralDataDiffReportWriterTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-gd-report-tests", Guid.NewGuid().ToString("N"));

    private static HtmlGeneralDataDiffReportWriter Writer() => new();

    private static HtmlDiffGeneralDataReportData EmptyReport() => new(
        Title: "General Data Diff Report",
        PreviousWorkbookPath: @"C:\prev\workbook.xlsx",
        CurrentWorkbookPath: @"C:\curr\workbook.xlsx",
        GeneratedAt: new DateTimeOffset(2025, 5, 28, 12, 0, 0, TimeSpan.Zero),
        Added: [],
        Removed: [],
        Changed: [],
        Unchanged: []
    );

    private static HtmlDiffGeneralDataQuestion MakeQuestion(
        string questionText = "What is the scope?",
        string? questionNumber = "1.1",
        string section = "Section A",
        int rowNumber = 5,
        IReadOnlyList<HtmlDiffGeneralDataAnswerCell>? answerCells = null,
        IReadOnlyList<HtmlDiffGeneralDataExplanationCell>? explanationCells = null) =>
        new(
            QuestionNumber: questionNumber,
            Section: section,
            RowNumber: rowNumber,
            QuestionText: questionText,
            RowNumberLabels: [questionNumber ?? ""],
            AnswerCells: answerCells ?? [],
            ExplanationCells: explanationCells ?? []
        );

    private static HtmlDiffGeneralDataAnswerCell MakeAnswerCell(
        int rowOffset = 0,
        string column = "D",
        string text = "Yes",
        string dvDisplay = "—",
        string? cfOperator = null) =>
        new(RowOffset: rowOffset, Column: column, Text: text, DvDisplay: dvDisplay, CfOperator: cfOperator);

    private static HtmlDiffGeneralDataExplanationCell MakeExplanationCell(
        int rowOffset = 0,
        string text = "Please explain",
        string dvDisplay = "—",
        string? cfOperator = null) =>
        new(RowOffset: rowOffset, Text: text, DvDisplay: dvDisplay, CfOperator: cfOperator);

    private static HtmlDiffGeneralDataChangedQuestion MakeChangedQuestion(
        string oldText = "Old question text",
        string newText = "New question text",
        string section = "Section A",
        IReadOnlyList<HtmlDiffAnswerCellChange>? answerCellChanges = null,
        IReadOnlyList<HtmlDiffExplanationCellChange>? explanationCellChanges = null) =>
        new(
            Section: section,
            PreviousRowNumber: 5,
            CurrentRowNumber: 5,
            PreviousNumber: "1.1",
            CurrentNumber: "1.1",
            OldText: oldText,
            NewText: newText,
            SimilarityScore: 0.85,
            SecondBestSimilarity: null,
            TextChanged: oldText != newText,
            NumberChanged: false,
            AnswerCellsChanged: answerCellChanges?.Count > 0,
            ExplanationCellsChanged: explanationCellChanges?.Count > 0,
            AnswerCellChanges: answerCellChanges ?? [],
            ExplanationCellChanges: explanationCellChanges ?? []
        );

    private static HtmlDiffAnswerCellChange MakeAnswerCellChange(
        string? oldText = "Old label",
        string? newText = "New label",
        int rowOffset = 0,
        string column = "D",
        bool dvChanged = false,
        bool cfChanged = false) =>
        new(
            RowOffset: rowOffset,
            Column: column,
            OldText: oldText,
            NewText: newText,
            OldDvDisplay: "—",
            NewDvDisplay: "—",
            OldCfOperator: null,
            NewCfOperator: null,
            TextChanged: oldText != newText,
            DvChanged: dvChanged,
            CfChanged: cfChanged
        );

    private static HtmlDiffExplanationCellChange MakeExplanationCellChange(
        string? oldText = "Old explanation",
        string? newText = "New explanation",
        int rowOffset = 0,
        bool dvChanged = false,
        bool cfChanged = false) =>
        new(
            RowOffset: rowOffset,
            OldText: oldText,
            NewText: newText,
            OldDvDisplay: "—",
            NewDvDisplay: "—",
            OldCfOperator: null,
            NewCfOperator: null,
            TextChanged: oldText != newText,
            DvChanged: dvChanged,
            CfChanged: cfChanged
        );

    private static HtmlDiffGeneralDataUnchangedQuestion MakeUnchangedQuestion(
        string questionText = "Unchanged question",
        string? questionNumber = "2.1",
        string section = "Section B") =>
        new(
            Section: section,
            PreviousRowNumber: 10,
            CurrentRowNumber: 10,
            QuestionNumber: questionNumber,
            QuestionText: questionText,
            RowNumberLabels: [questionNumber ?? ""],
            AnswerCells: [],
            ExplanationCells: [],
            SimilarityScore: 1.0,
            SecondBestSimilarity: null
        );

    // ── Empty report ───────────────────────────────────────────────────────────

    [Fact]
    public void WriteReport_EmptyData_CreatesFileWithZeroCounts()
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
            content.Should().Contain(">0<");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_CreatesParentDirectoryIfMissing()
    {
        var dir = TestWorkDir();
        try
        {
            var filePath = Path.Combine(dir, "subdir", "report.html");
            Writer().WriteReport(EmptyReport(), filePath);
            File.Exists(filePath).Should().BeTrue();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Added questions ────────────────────────────────────────────────────────

    [Fact]
    public void WriteReport_OneAddedQuestion_QuestionTextAndCellLabelsAppear()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var q = MakeQuestion(
                questionText: "What is the risk level?",
                answerCells: [MakeAnswerCell(text: "Low / Medium / High")]);
            var data = EmptyReport() with { Added = [q] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("What is the risk level?");
            content.Should().Contain("Low / Medium / High");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_OneRemovedQuestion_AppearsInFile()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var q = MakeQuestion(questionText: "This question was removed");
            var data = EmptyReport() with { Removed = [q] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("This question was removed");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_AddedQuestionWithExplanationCell_ExplanationTextAppears()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var q = MakeQuestion(
                questionText: "Rate the control",
                explanationCells: [MakeExplanationCell(text: "Provide rationale for your rating")]);
            var data = EmptyReport() with { Added = [q] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("Provide rationale for your rating");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Changed questions ──────────────────────────────────────────────────────

    [Fact]
    public void WriteReport_ChangedQuestionTextChange_BothOldAndNewTextAppear()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var c = MakeChangedQuestion(oldText: "Old question text here", newText: "New question text here");
            var data = EmptyReport() with { Changed = [c] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("Old");
            content.Should().Contain("New");
            content.Should().Contain("question text here");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_ChangedQuestionWithAnswerCellChange_OldAndNewLabelsAppear()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var cellChange = MakeAnswerCellChange(oldText: "Effective", newText: "Partially effective");
            var c = MakeChangedQuestion(answerCellChanges: [cellChange]);
            var data = EmptyReport() with { Changed = [c] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("Effective");
            content.Should().Contain("Partially effective");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_ChangedQuestionWithAddedCell_NewLabelAppears()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var cellChange = MakeAnswerCellChange(oldText: null, newText: "Brand new cell label");
            var c = MakeChangedQuestion(answerCellChanges: [cellChange]);
            var data = EmptyReport() with { Changed = [c] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("Brand new cell label");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_ChangedQuestionWithRemovedCell_OldLabelAppears()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var cellChange = MakeAnswerCellChange(oldText: "Cell that was removed", newText: null);
            var c = MakeChangedQuestion(answerCellChanges: [cellChange]);
            var data = EmptyReport() with { Changed = [c] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("Cell that was removed");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_ChangedQuestionWithDvChange_DvDisplaysAppear()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var cellChange = new HtmlDiffAnswerCellChange(
                RowOffset: 0, Column: "D",
                OldText: "Yes", NewText: "Yes",
                OldDvDisplay: "List: Yes | No",
                NewDvDisplay: "List: Yes | No | N/A",
                OldCfOperator: null, NewCfOperator: null,
                TextChanged: false, DvChanged: true, CfChanged: false);
            var c = MakeChangedQuestion(answerCellChanges: [cellChange]);
            var data = EmptyReport() with { Changed = [c] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("List: Yes | No");
            content.Should().Contain("List: Yes | No | N/A");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_ChangedQuestionWithCfChange_CfOperatorsAppear()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var cellChange = new HtmlDiffAnswerCellChange(
                RowOffset: 0, Column: "E",
                OldText: "text", NewText: "text",
                OldDvDisplay: "—", NewDvDisplay: "—",
                OldCfOperator: "Equal", NewCfOperator: "Between",
                TextChanged: false, DvChanged: false, CfChanged: true);
            var c = MakeChangedQuestion(answerCellChanges: [cellChange]);
            var data = EmptyReport() with { Changed = [c] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("Equal");
            content.Should().Contain("Between");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_ChangedQuestionWithExplanationCellChange_ExplanationChangeAppears()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var expChange = MakeExplanationCellChange(
                oldText: "Old explanation prompt",
                newText: "Updated explanation prompt");
            var c = MakeChangedQuestion(explanationCellChanges: [expChange]);
            var data = EmptyReport() with { Changed = [c] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("Old explanation prompt");
            content.Should().Contain("Updated explanation prompt");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Unchanged questions ────────────────────────────────────────────────────

    [Fact]
    public void WriteReport_OneUnchangedQuestion_AppearsWithSimilarity()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var u = MakeUnchangedQuestion(questionText: "This question is unchanged");
            var data = EmptyReport() with { Unchanged = [u] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("This question is unchanged");
            content.Should().Contain("100%");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Summary counts ─────────────────────────────────────────────────────────

    [Fact]
    public void WriteReport_SummaryCountsReflectData()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var data = new HtmlDiffGeneralDataReportData(
                Title: "Test",
                PreviousWorkbookPath: @"C:\prev.xlsx",
                CurrentWorkbookPath: @"C:\curr.xlsx",
                GeneratedAt: DateTimeOffset.UtcNow,
                Added:     [MakeQuestion(questionText: "Added Q")],
                Removed:   [MakeQuestion(questionText: "Removed Q")],
                Changed:   [MakeChangedQuestion()],
                Unchanged: [MakeUnchangedQuestion()]
            );
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain(">1<");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Security / encoding ────────────────────────────────────────────────────

    [Fact]
    public void WriteReport_EmbeddedJsonEscapesClosingScriptTag()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var q = MakeQuestion(questionText: "text with </script> tag");
            var data = EmptyReport() with { Added = [q] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            // System.Text.Json HTML-encodes < as <; the question text appears
            // in the JSON blob as unicode escapes, never as a raw </script> sequence.
            content.Should().Contain(@"</script>");
            content.Should().NotContain("text with </script> tag");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_HtmlSpecialCharsInQuestionText_AreEscaped()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var q = MakeQuestion(questionText: "Is a < b & c > d?");
            var data = EmptyReport() with { Added = [q] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("&lt;");
            content.Should().Contain("&amp;");
            content.Should().Contain("&gt;");
            content.Should().NotContain("Is a < b & c > d?");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_XssPayloadInQuestionText_IsNotRawInjectedIntoHtml()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var q = MakeQuestion(questionText: "<script>alert('PWN')</script>");
            var data = EmptyReport() with { Added = [q] };
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

    // ── Sheet-order and structure ──────────────────────────────────────────────

    [Fact]
    public void WriteReport_SectionAppearsInSheetOrderSeparator()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var q = MakeQuestion(questionText: "Q1", section: "General Controls");
            var data = EmptyReport() with { Added = [q] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("General Controls");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_AllSixTabsPresent()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(EmptyReport(), filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("showTab('added')");
            content.Should().Contain("showTab('removed')");
            content.Should().Contain("showTab('changed')");
            content.Should().Contain("showTab('unchanged')");
            content.Should().Contain("showTab('current-sheet')");
            content.Should().Contain("showTab('previous-sheet')");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_ContainsReportTitle()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var data = EmptyReport() with { Title = "My Special GD Report" };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("My Special GD Report");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteReport_MultipleAddedQuestions_AllTextsAppear()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var q1 = MakeQuestion(questionText: "First added question", rowNumber: 3);
            var q2 = MakeQuestion(questionText: "Second added question", rowNumber: 7);
            var q3 = MakeQuestion(questionText: "Third added question", rowNumber: 12);
            var data = EmptyReport() with { Added = [q1, q2, q3] };
            var filePath = Path.Combine(dir, "report.html");
            Writer().WriteReport(data, filePath);
            var content = File.ReadAllText(filePath);
            content.Should().Contain("First added question");
            content.Should().Contain("Second added question");
            content.Should().Contain("Third added question");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
