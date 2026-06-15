using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;
using Xunit;
using static ItrqTool.Tasks.Tests.QuestionnaireValidation.Parsing.ParsingTestSupport;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Parsing;

public sealed class QuestionParserTests
{
    // Single section: chapter at row 1, section header at row 2, questions 3–5.
    private static QuestionnaireLayout SingleSectionLayout() => new(
        QuestionTextColumn: "C",
        Chapters: [new LayoutChapter(1, "C")],
        Sections: [new LayoutSection(SectionRow: 2, FirstQuestionRow: 3, LastQuestionRow: 5, NameColumn: "C")]);

    [Fact]
    public void Parse_BuildsQuestionsViaFactory_WithContextFieldsPopulated()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(1, ("C", "Governance & Oversight")),
            Row(2, ("C", "Board Responsibilities")),
            Row(3, ("C", "1.1) How is governance documented?"), ("N", "GOV-01")),
            Row(4, ("C", "1.2) Who approves the policy?"), ("N", "GOV-02")),
        };
        var messages = new List<TaskMessage>();

        var result = QuestionParser.Parse(rows, SingleSectionLayout(), ClqStyleFactory, messages);

        result.Should().HaveCount(2);

        result[0].RowNumber.Should().Be(3);
        result[0].ChapterName.Should().Be("Governance & Oversight");
        result[0].SectionName.Should().Be("Board Responsibilities");
        result[0].XrefId.Should().Be("GOV-01");
        result[0].QuestionNumber.Should().Be("1.1");               // derived in the factory
        result[0].QuestionText.Should().Be("How is governance documented?");
        result[0].OriginalText.Should().Be("1.1) How is governance documented?");

        result[1].RowNumber.Should().Be(4);
        result[1].XrefId.Should().Be("GOV-02");
        result[1].QuestionNumber.Should().Be("1.2");

        messages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_BlankQuestionRowInRange_IsSkippedWithWarning()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(1, ("C", "Operational Resilience")),
            Row(2, ("C", "Continuity Planning")),
            Row(3, ("C", "1.1) Describe the recovery time objective"), ("N", "RES-01")),
            Row(4, ("C", "   ")),  // whitespace-only text → blank question row
        };
        var messages = new List<TaskMessage>();

        var result = QuestionParser.Parse(rows, SingleSectionLayout(), ClqStyleFactory, messages);

        result.Should().ContainSingle().Which.RowNumber.Should().Be(3);

        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Warning);
        messages[0].Text.Should().Be("Row 4: text column (C) is blank — row skipped.");
    }

    [Fact]
    public void Parse_RowsOutsideSectionRange_AreSkipped()
    {
        // Section declares questions 3–5; rows above (none here) and below 5 are not questions.
        var rows = new List<ExcelRowStructure>
        {
            Row(1, ("C", "Data Protection")),
            Row(2, ("C", "Records Management")),
            Row(3, ("C", "1.1) State the retention period"), ("N", "DP-01")),
            Row(6, ("C", "Stray populated cell past the section"), ("N", "DP-99")),
        };
        var messages = new List<TaskMessage>();

        var result = QuestionParser.Parse(rows, SingleSectionLayout(), ClqStyleFactory, messages);

        result.Should().ContainSingle().Which.RowNumber.Should().Be(3);
        messages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MultiSection_AssignsChapterAndSectionPerRow()
    {
        var layout = new QuestionnaireLayout(
            QuestionTextColumn: "C",
            Chapters: [new LayoutChapter(1, "C"), new LayoutChapter(5, "C")],
            Sections:
            [
                new LayoutSection(SectionRow: 2, FirstQuestionRow: 3, LastQuestionRow: 4, NameColumn: "C"),
                new LayoutSection(SectionRow: 6, FirstQuestionRow: 7, LastQuestionRow: 8, NameColumn: "C"),
            ]);

        var rows = new List<ExcelRowStructure>
        {
            Row(1, ("C", "Chapter Alpha")),
            Row(2, ("C", "Section One")),
            Row(3, ("C", "1.1) First question of section one"), ("N", "A1")),
            Row(4, ("C", "1.2) Second question of section one"), ("N", "A2")),
            Row(5, ("C", "Chapter Beta")),
            Row(6, ("C", "Section Two")),
            Row(7, ("C", "2.1) First question of section two"), ("N", "B1")),
            Row(8, ("C", "2.2) Second question of section two"), ("N", "B2")),
        };
        var messages = new List<TaskMessage>();

        var result = QuestionParser.Parse(rows, layout, ClqStyleFactory, messages);

        result.Should().HaveCount(4);

        result.Where(q => q.RowNumber is 3 or 4)
            .Should().OnlyContain(q => q.ChapterName == "Chapter Alpha" && q.SectionName == "Section One");
        result.Where(q => q.RowNumber is 7 or 8)
            .Should().OnlyContain(q => q.ChapterName == "Chapter Beta" && q.SectionName == "Section Two");

        messages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ReadsHeaderNamesFromConfiguredNameColumns()
    {
        // Chapter name read from column B, section name from column D, question text from C.
        var layout = new QuestionnaireLayout(
            QuestionTextColumn: "C",
            Chapters: [new LayoutChapter(1, "B")],
            Sections: [new LayoutSection(SectionRow: 2, FirstQuestionRow: 3, LastQuestionRow: 3, NameColumn: "D")]);

        var rows = new List<ExcelRowStructure>
        {
            Row(1, ("B", "Chapter from column B"), ("C", "ignored")),
            Row(2, ("C", "ignored"), ("D", "Section from column D")),
            Row(3, ("C", "1.1) Question text from column C"), ("N", "X1")),
        };
        var messages = new List<TaskMessage>();

        var result = QuestionParser.Parse(rows, layout, ClqStyleFactory, messages);

        var q = result.Should().ContainSingle().Subject;
        q.ChapterName.Should().Be("Chapter from column B");
        q.SectionName.Should().Be("Section from column D");
        q.QuestionText.Should().Be("Question text from column C");
        messages.Should().BeEmpty();
    }
}
