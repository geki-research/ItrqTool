using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.GeneralDataValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;
using Xunit;
using static ItrqTool.Tasks.Tests.QuestionnaireValidation.Parsing.ParsingTestSupport;

namespace ItrqTool.Tasks.Tests.GeneralDataValidationV02;

public sealed class GdV02QuestionParserTests
{
    // GD-v02 column map: C=number, D=text/section, E=guidance, F=requested-type, G=prev-answer,
    // H=answer, I=requested-expl, J=prev-expl, K=current-expl, L=material-change,
    // M=how-explanation, P=provided-by (shifted from v01's O), R=xref (shifted from v01's Q).
    private static GdV02Config Config() => new()
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
        HowExplanationColumn = "M",
        ProvidedByColumn = "P",
        XrefIdColumn = "R",
        SheetName = "General Data",
        Sections = [],
        DeviationThreshold = 0.25,
    };

    private static QuestionnaireLayout SingleSection() => new(
        QuestionTextColumn: "D",
        Chapters: [],
        Sections: [new LayoutSection(SectionRow: 2, FirstQuestionRow: 3, LastQuestionRow: 60, NameColumn: "D")]);

    private static GdV02ParseResult Parse(List<ExcelRowStructure> rows)
        => GdV02QuestionParser.Parse(rows, SingleSection(), Config(), new List<TaskMessage>());

    [Fact]
    public void Parse_BareQid_Collapsed_NoExplanation_OneImplicitAnswer()
    {
        var result = Parse(
        [
            Row(2, ("D", "Entities in scope")),
            Row(3, ("C", "1"), ("D", "Name of supervised entity"),
                   ("G", "prior"), ("H", "Erste Bank"), ("M", null), ("P", "Unit A"), ("R", "G-CO-1")),
        ]);

        result.Malformed.Should().BeEmpty();
        var q = result.Questions.Should().ContainSingle().Subject;
        q.XrefId.Should().Be("G-CO-1");
        q.RowNumber.Should().Be(3);
        q.QuestionNumber.Should().Be("1");
        q.OriginalText.Should().Be("Name of supervised entity");
        q.QuestionText.Should().Be("Name of supervised entity");
        q.SectionName.Should().Be("Entities in scope");

        var a = q.Answers.Should().ContainSingle().Subject;
        a.AnswerId.Should().BeNull();
        a.AnchorRow.Should().Be(3);
        a.PreviousAnswer.Should().Be("prior");
        a.Answer.Should().Be("Erste Bank");
        a.ProvidedBy.Should().Be("Unit A");
        a.HowExplanation.Should().BeNull();
        a.MaterialChange.Should().BeNull();
        a.Explanations.Should().Equal(new GdExplanationRow(null, null, null, 3));
    }

    [Fact]
    public void Parse_BareQid_WithOneExplanation_TripletCaptured()
    {
        var result = Parse(
        [
            Row(2, ("D", "Environment")),
            Row(3, ("C", "37"), ("D", "Critical findings"), ("H", "4"),
                   ("I", "Please clarify"), ("J", "last year"), ("K", "this year"), ("R", "G-EM-01")),
        ]);

        result.Malformed.Should().BeEmpty();
        var a = result.Questions.Should().ContainSingle().Subject.Answers.Should().ContainSingle().Subject;
        a.AnswerId.Should().BeNull();
        a.Explanations.Should().Equal(new GdExplanationRow("Please clarify", "last year", "this year", 3));
    }

    [Fact]
    public void Parse_MultiAnswer_PerAnswerAnchorAndOncePerAnswerCells()
    {
        // qnum 7: D merged over rows 3–4; M and P read once per answer at anchor row.
        var result = Parse(
        [
            Row(2, ("D", "Staff")),
            Row(3, ("C", "7"), ("D", "Total number of employees"), ("F", "Group FTEs"),
                   ("G", "g1"), ("H", "100"), ("L", "No"), ("M", "How-A1"), ("P", "Group"), ("R", "G-ST-01:A-01")),
            Row(4, ("F", "SE FTEs"),
                   ("G", "g2"), ("H", "50"), ("L", "Yes"), ("M", "How-A2"), ("P", "SE"), ("R", "G-ST-01:A-02")),
        ]);

        result.Malformed.Should().BeEmpty();
        var q = result.Questions.Should().ContainSingle().Subject;
        q.XrefId.Should().Be("G-ST-01");
        q.RowNumber.Should().Be(3);
        q.QuestionNumber.Should().Be("7");
        q.OriginalText.Should().Be("Total number of employees");
        q.SectionName.Should().Be("Staff");

        q.Answers.Should().HaveCount(2);
        var a1 = q.Answers[0];
        a1.AnswerId.Should().Be("A-01");
        a1.AnchorRow.Should().Be(3);
        a1.PreviousAnswer.Should().Be("g1");
        a1.Answer.Should().Be("100");
        a1.MaterialChange.Should().Be("No");
        a1.HowExplanation.Should().Be("How-A1");
        a1.ProvidedBy.Should().Be("Group");

        var a2 = q.Answers[1];
        a2.AnswerId.Should().Be("A-02");
        a2.AnchorRow.Should().Be(4);
        a2.PreviousAnswer.Should().Be("g2");
        a2.Answer.Should().Be("50");
        a2.MaterialChange.Should().Be("Yes");
        a2.HowExplanation.Should().Be("How-A2");
        a2.ProvidedBy.Should().Be("SE");
    }

    [Fact]
    public void Parse_MultiExplanationAnswer_PerRowTripletsUnderOneAnswer_MergedCellsOnAnchor()
    {
        // H/L/M/P merged on anchor row (10); blank on continuation row (11); I/K per-row.
        var result = Parse(
        [
            Row(2, ("D", "Staff")),
            Row(10, ("C", "12"), ("D", "Vacancies and turnover"), ("F", "# vacancies"),
                    ("H", "5"), ("L", "No"), ("M", "how-expl"), ("P", "U"),
                    ("I", "req1"), ("J", "p1"), ("K", "c1"), ("R", "G-ST-06-01:A-01:E-01")),
            Row(11, ("I", "req2"), ("K", "c2"), ("R", "G-ST-06-01:A-01:E-02")),
            Row(12, ("F", "avg months"), ("H", "3"), ("L", "No"), ("P", "U"), ("R", "G-ST-06-01:A-02")),
        ]);

        result.Malformed.Should().BeEmpty();
        var q = result.Questions.Should().ContainSingle().Subject;
        q.XrefId.Should().Be("G-ST-06-01");
        q.RowNumber.Should().Be(10);
        q.Answers.Should().HaveCount(2);

        var a1 = q.Answers[0];
        a1.AnswerId.Should().Be("A-01");
        a1.AnchorRow.Should().Be(10);
        a1.Answer.Should().Be("5");
        a1.MaterialChange.Should().Be("No");
        a1.HowExplanation.Should().Be("how-expl");   // merged value on anchor; not overwritten by row 11
        a1.Explanations.Should().Equal(
            new GdExplanationRow("req1", "p1", "c1", 10),
            new GdExplanationRow("req2", null, "c2", 11));

        var a2 = q.Answers[1];
        a2.AnswerId.Should().Be("A-02");
        a2.AnchorRow.Should().Be(12);
        a2.Answer.Should().Be("3");
        a2.HowExplanation.Should().BeNull();
        a2.Explanations.Should().Equal(new GdExplanationRow(null, null, null, 12));
    }

    [Fact]
    public void Parse_InterleavedNonContiguousQids_GroupedByDictionary_NotContiguousRun()
    {
        var result = Parse(
        [
            Row(2, ("D", "Financials")),
            Row(20, ("C", "18a&b"), ("D", "Security expenses"), ("F", "Overall"),
                    ("H", "1000"), ("P", "GC"), ("R", "G-FI-02-01-1:A-01")),
            Row(21, ("F", "Physical"), ("H", "200"), ("P", "GC"), ("R", "G-FI-02-01-2:A-01")),
            Row(22, ("F", "Overall budget"), ("H", "1100"), ("P", "GC"), ("R", "G-FI-02-01-1:A-02")),
            Row(23, ("F", "Physical budget"), ("H", "250"), ("P", "GC"), ("R", "G-FI-02-01-2:A-02")),
        ]);

        result.Malformed.Should().BeEmpty();
        result.Questions.Should().HaveCount(2);

        var q1 = result.Questions[0];
        q1.XrefId.Should().Be("G-FI-02-01-1");
        q1.RowNumber.Should().Be(20);
        q1.OriginalText.Should().Be("Security expenses");
        q1.QuestionNumber.Should().Be("18a&b");
        q1.Answers.Select(a => a.AnswerId).Should().Equal("A-01", "A-02");
        q1.Answers.Select(a => a.AnchorRow).Should().Equal(20, 22);
        q1.Answers.Select(a => a.Answer).Should().Equal("1000", "1100");

        var q2 = result.Questions[1];
        q2.XrefId.Should().Be("G-FI-02-01-2");
        q2.RowNumber.Should().Be(21);
        q2.OriginalText.Should().Be("Security expenses");
        q2.QuestionNumber.Should().Be("18a&b");
        q2.Answers.Select(a => a.AnswerId).Should().Equal("A-01", "A-02");
        q2.Answers.Select(a => a.AnchorRow).Should().Equal(21, 23);
        q2.Answers.Select(a => a.Answer).Should().Equal("200", "250");
    }

    [Fact]
    public void Parse_MultiQidPerDisplayBlock_BothShareDAndC_DistinctIdentity()
    {
        var result = Parse(
        [
            Row(2, ("D", "Staff")),
            Row(30, ("C", "12"), ("D", "Vacancies and turnover"), ("F", "# vacancies"), ("H", "5"), ("R", "G-ST-06-01:A-01")),
            Row(31, ("F", "avg months"), ("H", "3"), ("R", "G-ST-06-01:A-02")),
            Row(32, ("F", "% turnover"), ("H", "10"), ("R", "G-ST-06-02:A-01")),
            Row(33, ("F", "% total turnover"), ("H", "12"), ("R", "G-ST-06-02:A-02")),
        ]);

        result.Malformed.Should().BeEmpty();
        result.Questions.Should().HaveCount(2);

        var q1 = result.Questions[0];
        q1.XrefId.Should().Be("G-ST-06-01");
        q1.RowNumber.Should().Be(30);
        q1.QuestionNumber.Should().Be("12");
        q1.OriginalText.Should().Be("Vacancies and turnover");
        q1.Answers.Select(a => a.AnchorRow).Should().Equal(30, 31);

        var q2 = result.Questions[1];
        q2.XrefId.Should().Be("G-ST-06-02");
        q2.RowNumber.Should().Be(32);
        q2.QuestionNumber.Should().Be("12");
        q2.OriginalText.Should().Be("Vacancies and turnover");
        q2.Answers.Select(a => a.AnchorRow).Should().Equal(32, 33);
    }

    [Fact]
    public void Parse_BlankXref_LandsInMalformed_NotBucketed()
    {
        var result = Parse(
        [
            Row(2, ("D", "Entities")),
            Row(3, ("C", "1"), ("D", "Valid"), ("H", "x"), ("R", "G-CO-1")),
            Row(4, ("D", "Orphan"), ("H", "y"), ("R", "   ")),
        ]);

        var q = result.Questions.Should().ContainSingle().Subject;
        q.XrefId.Should().Be("G-CO-1");
        q.Answers.Should().ContainSingle();

        result.Malformed.Should().ContainSingle()
            .Which.Should().Be(new GdMalformedXref(4, null, GdMalformedXrefReason.Blank));
    }

    [Fact]
    public void Parse_DuplicateFullXref_AllOccurrencesFlagged()
    {
        var result = Parse(
        [
            Row(2, ("D", "Staff")),
            Row(3, ("C", "7"), ("D", "Q one"), ("H", "a"), ("R", "G-ST-01:A-01")),
            Row(4, ("F", "f"), ("H", "b"), ("R", "G-ST-01:A-01")),
        ]);

        result.Malformed.Should().Equal(
            new GdMalformedXref(3, "G-ST-01:A-01", GdMalformedXrefReason.Duplicate),
            new GdMalformedXref(4, "G-ST-01:A-01", GdMalformedXrefReason.Duplicate));

        var q = result.Questions.Should().ContainSingle().Subject;
        q.XrefId.Should().Be("G-ST-01");
        var a = q.Answers.Should().ContainSingle().Subject;
        a.AnswerId.Should().Be("A-01");
        a.Answer.Should().Be("a");
        a.Explanations.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_UnparseableXref_EmptyQid_TrailingColon_TooDeep_AllFlagged()
    {
        var result = Parse(
        [
            Row(2, ("D", "Staff")),
            Row(3, ("D", "valid"), ("R", "G-CO-1")),
            Row(4, ("D", "empty qid"), ("R", ":A-01")),
            Row(5, ("D", "trailing colon"), ("R", "G-ST-01:")),
            Row(6, ("D", "too deep"), ("R", "G:A:E:X")),
        ]);

        result.Questions.Should().ContainSingle().Which.XrefId.Should().Be("G-CO-1");
        result.Malformed.Should().Equal(
            new GdMalformedXref(4, ":A-01", GdMalformedXrefReason.Unparseable),
            new GdMalformedXref(5, "G-ST-01:", GdMalformedXrefReason.Unparseable),
            new GdMalformedXref(6, "G:A:E:X", GdMalformedXrefReason.Unparseable));
    }

    [Fact]
    public void Parse_TwoSections_AssignsSectionNamePerQuestion_SkipsOutOfExtent()
    {
        var layout = new QuestionnaireLayout(
            QuestionTextColumn: "D",
            Chapters: [],
            Sections:
            [
                new LayoutSection(SectionRow: 2, FirstQuestionRow: 3, LastQuestionRow: 4, NameColumn: "D"),
                new LayoutSection(SectionRow: 6, FirstQuestionRow: 7, LastQuestionRow: 8, NameColumn: "D"),
            ]);

        var rows = new List<ExcelRowStructure>
        {
            Row(2, ("D", "Section One")),
            Row(3, ("C", "1"), ("D", "Q one-a"), ("R", "G-CO-1")),
            Row(4, ("C", "2"), ("D", "Q one-b"), ("R", "G-CO-2")),
            Row(5, ("D", "stray outside any extent"), ("R", "G-ZZ-9")),
            Row(6, ("D", "Section Two")),
            Row(7, ("C", "3"), ("D", "Q two-a"), ("R", "G-ST-1")),
            Row(8, ("C", "4"), ("D", "Q two-b"), ("R", "G-ST-2")),
        };

        var result = GdV02QuestionParser.Parse(rows, layout, Config(), new List<TaskMessage>());

        result.Malformed.Should().BeEmpty();
        result.Questions.Select(q => q.XrefId).Should().Equal("G-CO-1", "G-CO-2", "G-ST-1", "G-ST-2");
        result.Questions.Where(q => q.RowNumber is 3 or 4)
            .Should().OnlyContain(q => q.SectionName == "Section One");
        result.Questions.Where(q => q.RowNumber is 7 or 8)
            .Should().OnlyContain(q => q.SectionName == "Section Two");
        result.Questions.Should().NotContain(q => q.XrefId == "G-ZZ-9");
    }

    private static GdV02Config ConfigDeclaring(string expectedName) => new()
    {
        QuestionNumberColumn = "C", TextColumn = "D", GuidanceColumn = "E",
        RequestedTypeColumn = "F", PreviousAnswerColumn = "G", AnswerColumn = "H",
        RequestedExplanationColumn = "I", PreviousExplanationColumn = "J",
        CurrentExplanationColumn = "K", MaterialChangeColumn = "L",
        HowExplanationColumn = "M", ProvidedByColumn = "P", XrefIdColumn = "R",
        SheetName = "General Data",
        Sections = [new GdSectionSpec(HeaderRow: 2, FirstDataRow: 3, LastDataRow: 60, ExpectedName: expectedName, MaterialChangeRequired: false)],
        DeviationThreshold = 0.25,
    };

    [Fact]
    public void Parse_SectionHeaderMismatch_RecordedWhenHeaderDiffersFromExpected()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(2, ("D", "Stff")),
            Row(3, ("C", "1"), ("D", "Q one"), ("R", "G-ST-1")),
        };

        var result = GdV02QuestionParser.Parse(rows, SingleSection(), ConfigDeclaring("Staff"), new List<TaskMessage>());

        result.SectionHeaderMismatches.Should().ContainSingle()
            .Which.Should().Be(new GdSectionHeaderMismatch(2, "D", "Staff", "Stff"));
        result.Questions.Should().ContainSingle().Which.XrefId.Should().Be("G-ST-1");
        result.Malformed.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SectionHeaderMismatch_BlankHeaderRecordedAsNullActual()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(2 /* no D cell → blank header */),
            Row(3, ("C", "1"), ("D", "Q one"), ("R", "G-ST-1")),
        };

        var result = GdV02QuestionParser.Parse(rows, SingleSection(), ConfigDeclaring("Staff"), new List<TaskMessage>());

        result.SectionHeaderMismatches.Should().ContainSingle()
            .Which.Should().Be(new GdSectionHeaderMismatch(2, "D", "Staff", null));
    }

    [Fact]
    public void Parse_SectionHeaderMatch_NoMismatch()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(2, ("D", "Staff")),
            Row(3, ("C", "1"), ("D", "Q one"), ("R", "G-ST-1")),
        };

        var result = GdV02QuestionParser.Parse(rows, SingleSection(), ConfigDeclaring("Staff"), new List<TaskMessage>());

        result.SectionHeaderMismatches.Should().BeEmpty();
    }

    [Fact]
    public void Parse_HowExplanation_ReadAtAnchorRow_NotOverwrittenByContinuationRow()
    {
        // Answer A-01 spans two rows. HowExplanation (M) is merged on the anchor (row 3);
        // row 4 has no M value (continuation). The anchor value must survive.
        var result = Parse(
        [
            Row(2, ("D", "Staff")),
            Row(3, ("C", "9"), ("D", "Q text"), ("H", "42"), ("M", "how-text"), ("R", "G-ST-09:A-01:E-01")),
            Row(4, ("R", "G-ST-09:A-01:E-02")),
        ]);

        result.Malformed.Should().BeEmpty();
        var q = result.Questions.Should().ContainSingle().Subject;
        var a = q.Answers.Should().ContainSingle().Subject;
        a.AnswerId.Should().Be("A-01");
        a.AnchorRow.Should().Be(3);
        a.HowExplanation.Should().Be("how-text");
        a.Explanations.Should().HaveCount(2);
    }
}
