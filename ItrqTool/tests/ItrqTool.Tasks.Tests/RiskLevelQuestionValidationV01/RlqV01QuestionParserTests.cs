using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using Xunit;
using static ItrqTool.Tasks.Tests.QuestionnaireValidation.Parsing.ParsingTestSupport;

namespace ItrqTool.Tasks.Tests.RiskLevelQuestionValidationV01;

public sealed class RlqV01QuestionParserTests
{
    // RLQ column map: C=number, D=text/section, E=guidance, F=requested-type, G=prev-answer,
    // H=answer, I=requested-expl, J=prev-expl, K=current-expl, L=material-change,
    // O=provided-by, Q=xref.
    private static RlqV01Config Config() => new()
    {
        QuestionNumberColumn = "C",
        TextColumn = "D",
        GuidanceColumn = "E",
        RequestedTypeColumn = "F",
        PreviousAnswerColumn = "G",
        AnswerColumn = "H",
        RequestedExplanationColumn = "I",
        PreviousExplanationColumn = "J",
        CurrentExplanationColumn = "K",
        MaterialChangeColumn = "L",
        ProvidedByColumn = "O",
        XrefIdColumn = "Q",
        SheetName = "IT Risk Level Questions",
        SectionRows = ["2:3-10"],
    };

    // Section header at row 2; question rows 3–10.
    private static QuestionnaireLayout SingleSection() => new(
        QuestionTextColumn: "D",
        Chapters: [],
        Sections: [new LayoutSection(SectionRow: 2, FirstQuestionRow: 3, LastQuestionRow: 10, NameColumn: "D")]);

    [Fact]
    public void Parse_SingleRowQuestion_NoExplanations_OneRecord_WithAllBlankTriplet()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(2, ("D", "Section One")),
            Row(3, ("C", "1"), ("D", "First risk question"), ("E", "guide"), ("F", "Text"),
                   ("G", "prior"), ("H", "Yes"), ("L", "No"), ("O", "Unit A"), ("Q", "RLQ-1")),
        };
        var messages = new List<TaskMessage>();

        var result = RlqV01QuestionParser.Parse(rows, SingleSection(), Config(), messages);

        var q = result.Should().ContainSingle().Subject;
        q.RowNumber.Should().Be(3);
        q.XrefId.Should().Be("RLQ-1");
        q.QuestionNumber.Should().Be("1");
        q.OriginalText.Should().Be("First risk question");
        q.QuestionText.Should().Be("First risk question");
        q.SectionName.Should().Be("Section One");
        q.Guidance.Should().Be("guide");
        q.RequestedType.Should().Be("Text");
        q.PreviousAnswer.Should().Be("prior");
        q.Answer.Should().Be("Yes");
        q.MaterialChange.Should().Be("No");
        q.ProvidedBy.Should().Be("Unit A");
        q.AnswerDvType.Should().BeNull();
        q.AnswerDvFormula.Should().BeNull();
        q.AnswerDvOperator.Should().BeNull();
        q.AnswerDvFormula2.Should().BeNull();

        // Every group row contributes one triplet; a 0-explanation question still occupies
        // one row, whose triplet is all-blank.
        q.ExplanationRows.Should().ContainSingle()
            .Which.Should().Be(new RlqExplanationRow(null, null, null, 3));

        messages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SingleRowQuestion_OneExplanation_OneRecord_WithOneTriplet()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(2, ("D", "Section One")),
            Row(3, ("C", "1"), ("D", "Explained question"), ("H", "No"),
                   ("I", "Please explain"), ("J", "last year"), ("K", "this year"), ("Q", "RLQ-2")),
        };
        var messages = new List<TaskMessage>();

        var result = RlqV01QuestionParser.Parse(rows, SingleSection(), Config(), messages);

        var q = result.Should().ContainSingle().Subject;
        q.RowNumber.Should().Be(3);
        q.ExplanationRows.Should().ContainSingle()
            .Which.Should().Be(new RlqExplanationRow("Please explain", "last year", "this year", 3));
        messages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MultiRowQuestion_ThreeExplanations_OneRecord_FirstRowFields_TripletsInOrder()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(2, ("D", "Section One")),
            // First (top) row carries every once-per-question field plus the first triplet.
            Row(3, ("C", "1"), ("D", "Spanning question"), ("E", "g"), ("F", "Text"),
                   ("G", "pa"), ("H", "Yes"), ("L", "No"), ("O", "Unit A"),
                   ("I", "req1"), ("J", "p1"), ("K", "c1"), ("Q", "RLQ-3")),
            // Continuation rows: once-per-question columns are blank (merged); only I/J/K + Q present.
            Row(4, ("I", "req2"), ("J", "p2"), ("K", "c2"), ("Q", "RLQ-3")),
            Row(5, ("I", "req3"), ("J", "p3"), ("K", "c3"), ("Q", "RLQ-3")),
        };
        var messages = new List<TaskMessage>();

        var result = RlqV01QuestionParser.Parse(rows, SingleSection(), Config(), messages);

        var q = result.Should().ContainSingle().Subject;
        q.RowNumber.Should().Be(3);                    // first row of the group
        q.OriginalText.Should().Be("Spanning question"); // once-per-question fields from first row
        q.QuestionNumber.Should().Be("1");
        q.Guidance.Should().Be("g");
        q.RequestedType.Should().Be("Text");
        q.PreviousAnswer.Should().Be("pa");
        q.Answer.Should().Be("Yes");
        q.MaterialChange.Should().Be("No");
        q.ProvidedBy.Should().Be("Unit A");

        q.ExplanationRows.Should().Equal(
            new RlqExplanationRow("req1", "p1", "c1", 3),
            new RlqExplanationRow("req2", "p2", "c2", 4),
            new RlqExplanationRow("req3", "p3", "c3", 5));

        messages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_TwoAdjacentQuestions_DifferentXrefIds_TwoRecords()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(2, ("D", "Section One")),
            Row(3, ("D", "Question A"), ("Q", "RLQ-A")),
            Row(4, ("D", "Question B"), ("Q", "RLQ-B")),
        };
        var messages = new List<TaskMessage>();

        var result = RlqV01QuestionParser.Parse(rows, SingleSection(), Config(), messages);

        result.Should().HaveCount(2);
        result[0].RowNumber.Should().Be(3);
        result[0].XrefId.Should().Be("RLQ-A");
        result[0].OriginalText.Should().Be("Question A");
        result[1].RowNumber.Should().Be(4);
        result[1].XrefId.Should().Be("RLQ-B");
        result[1].OriginalText.Should().Be("Question B");
        messages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ContiguousEqualXrefId_CollapsesToOneRecord()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(2, ("D", "Section One")),
            Row(3, ("D", "Duplicate-xref question"), ("Q", "RLQ-D")),
            Row(4, ("Q", "RLQ-D")), // continuation
        };
        var messages = new List<TaskMessage>();

        var result = RlqV01QuestionParser.Parse(rows, SingleSection(), Config(), messages);

        var q = result.Should().ContainSingle().Subject;
        q.RowNumber.Should().Be(3);
        q.XrefId.Should().Be("RLQ-D");
        q.ExplanationRows.Should().HaveCount(2); // two rows in the group → two triplets
        messages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SameXrefId_NonContiguous_YieldsTwoSeparateRecords()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(2, ("D", "Section One")),
            Row(3, ("D", "X first"), ("Q", "RLQ-X")),
            Row(4, ("D", "Y between"), ("Q", "RLQ-Y")),
            Row(5, ("D", "X second"), ("Q", "RLQ-X")),
        };
        var messages = new List<TaskMessage>();

        var result = RlqV01QuestionParser.Parse(rows, SingleSection(), Config(), messages);

        result.Should().HaveCount(3);
        // The two RLQ-X occurrences are distinct records (later the alignment engine flags
        // the genuine duplicate); we assert separation here, not a finding.
        result.Where(q => q.XrefId == "RLQ-X")
            .Select(q => q.RowNumber)
            .Should().Equal(3, 5);
        messages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_TwoSections_AssignsSectionNamePerRow()
    {
        var layout = new QuestionnaireLayout(
            QuestionTextColumn: "D",
            Chapters: [],
            Sections:
            [
                new LayoutSection(SectionRow: 2, FirstQuestionRow: 3, LastQuestionRow: 4, NameColumn: "D"),
                new LayoutSection(SectionRow: 5, FirstQuestionRow: 6, LastQuestionRow: 7, NameColumn: "D"),
            ]);

        var rows = new List<ExcelRowStructure>
        {
            Row(2, ("D", "Section One")),
            Row(3, ("D", "Q one-a"), ("Q", "RLQ-1")),
            Row(4, ("D", "Q one-b"), ("Q", "RLQ-2")),
            Row(5, ("D", "Section Two")),
            Row(6, ("D", "Q two-a"), ("Q", "RLQ-3")),
            Row(7, ("D", "Q two-b"), ("Q", "RLQ-4")),
        };
        var messages = new List<TaskMessage>();

        var result = RlqV01QuestionParser.Parse(rows, layout, Config(), messages);

        result.Should().HaveCount(4);
        result.Where(q => q.RowNumber is 3 or 4)
            .Should().OnlyContain(q => q.SectionName == "Section One");
        result.Where(q => q.RowNumber is 6 or 7)
            .Should().OnlyContain(q => q.SectionName == "Section Two");
        messages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_BlankXrefIdQuestionRow_EmitsDegenerateNullKeyRecord_AndDoesNotFoldIntoNeighbour()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(2, ("D", "Section One")),
            Row(3, ("D", "Valid question"), ("Q", "RLQ-1")),
            Row(4, ("D", "Orphan with no xref"), ("Q", "   ")), // whitespace-only xref
        };
        var messages = new List<TaskMessage>();

        var result = RlqV01QuestionParser.Parse(rows, SingleSection(), Config(), messages);

        // Blank-xref row emits a degenerate null-key record; RLQ-1 is NOT extended by it.
        result.Should().HaveCount(2);

        var valid = result[0];
        valid.RowNumber.Should().Be(3);
        valid.XrefId.Should().Be("RLQ-1");
        valid.ExplanationRows.Should().ContainSingle(); // RLQ-1 closed before the blank row

        var degenerate = result[1];
        degenerate.RowNumber.Should().Be(4);
        degenerate.XrefId.Should().BeNull(); // null-key record ready for ClassifyKeys → Blank

        messages.Should().BeEmpty(); // warning replaced by Fatal finding via MalformedKeyCheck
    }

    [Fact]
    public void Parse_BlankXrefIdMidMultiRowQuestion_SplitsGroup_EmitsDegenerateRecord()
    {
        // A multi-row question "RLQ-X" intended across rows 3–5 with row 4's XrefId blank.
        // The blank row closes the in-progress group (row 3 only) and becomes a degenerate
        // record; row 5 starts a fresh RLQ-X group. Parser emits 3 records.
        var rows = new List<ExcelRowStructure>
        {
            Row(2, ("D", "Section One")),
            Row(3, ("D", "Spanning question"), ("Q", "RLQ-X")),
            Row(4, ("D", "Blank-xref row"),    ("Q", "")),      // blank XrefId mid-group
            Row(5, ("I", "req2"),              ("Q", "RLQ-X")), // resumes after blank
        };
        var messages = new List<TaskMessage>();

        var result = RlqV01QuestionParser.Parse(rows, SingleSection(), Config(), messages);

        result.Should().HaveCount(3);

        result[0].RowNumber.Should().Be(3);
        result[0].XrefId.Should().Be("RLQ-X");
        result[0].ExplanationRows.Should().ContainSingle(); // group closed at the blank row

        result[1].RowNumber.Should().Be(4);
        result[1].XrefId.Should().BeNull(); // degenerate null-key record

        result[2].RowNumber.Should().Be(5);
        result[2].XrefId.Should().Be("RLQ-X"); // fresh group after the blank

        messages.Should().BeEmpty();
    }
}
