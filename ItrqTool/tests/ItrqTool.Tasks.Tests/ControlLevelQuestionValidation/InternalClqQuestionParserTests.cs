using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Tasks.ControlLevelQuestionValidation;
using Xunit;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidation;

public sealed class InternalClqQuestionParserTests
{
    private static ControlLevelQuestionValidationV01Config MakeConfig() => new()
    {
        TextColumn = "D",
        GuidanceColumn = "E",
        PreviousAnswerColumn = "F",
        AnswerColumn = "H",
        StrengthsColumn = "I",
        WeaknessesColumn = "J",
        ProvidedByColumn = "M",
        XrefIdColumn = "N",
        SheetName = "CLQ",
        ChapterRows = [1],
        SectionRows = ["2:3-8"],
        AllowedAnswers = ["1", "2", "3", "4"],
        DeviationThreshold = 2
    };

    private static ExcelRowStructure MakeRow(int rowNum, Dictionary<string, ExcelCellStructure> cells)
        => new(rowNum, cells);

    private static ExcelCellStructure TextCell(string? text)
        => new(text, null, null, null);

    private static ExcelCellStructure DvListCell(string? text, string dvFormula)
        => new(text, "List", dvFormula, null);

    private static ExcelCellStructure FullCell(
        string? text,
        string? dvType = null,
        string? dvFormula = null,
        string? cfOperator = null,
        string? dvOperator = null,
        string? dvFormula2 = null,
        string? cfType = null,
        string? cfValue = null,
        string? cfValue2 = null)
        => new(text, dvType, dvFormula, cfOperator, dvOperator, dvFormula2, cfType, cfValue, cfValue2);

    [Fact]
    public void Parse_FullScenario_ReturnsCorrectQuestions()
    {
        var config = MakeConfig();
        var rows = new List<ExcelRowStructure>
        {
            // Chapter row
            MakeRow(1, new() { ["D"] = TextCell("Chapter A") }),
            // Section row
            MakeRow(2, new() { ["D"] = TextCell("Section 1") }),
            // Question 1: normal, with DV list on H, guidance and XrefId
            MakeRow(3, new()
            {
                ["D"] = TextCell("1.1) What is the risk?"),
                ["E"] = TextCell("Guidance text"),
                ["F"] = TextCell("Old answer"),
                ["H"] = DvListCell("2", "\"1,2,3,4\""),
                ["I"] = TextCell("Strength noted"),
                ["J"] = TextCell("Weakness noted"),
                ["M"] = TextCell("Unit A"),
                ["N"] = TextCell("REF-001")
            }),
            // Question 2: no-paren prefix, blank answer
            MakeRow(4, new()
            {
                ["D"] = TextCell("1.2 Another question"),
                ["H"] = TextCell(null)
            }),
        };

        var messages = new List<TaskMessage>();
        var questions = InternalClqQuestionParser.Parse(rows, config, messages);

        questions.Should().HaveCount(2);
        messages.Should().BeEmpty();

        var q1 = questions[0];
        q1.RowNumber.Should().Be(3);
        q1.ChapterName.Should().Be("Chapter A");
        q1.SectionName.Should().Be("Section 1");
        q1.QuestionNumber.Should().Be("1.1");
        q1.QuestionText.Should().Be("What is the risk?");
        q1.OriginalText.Should().Be("1.1) What is the risk?");
        q1.Guidance.Should().Be("Guidance text");
        q1.PreviousAnswer.Should().Be("Old answer");
        q1.Answer.Should().Be("2");
        q1.Strengths.Should().Be("Strength noted");
        q1.Weaknesses.Should().Be("Weakness noted");
        q1.ProvidedBy.Should().Be("Unit A");
        q1.XrefId.Should().Be("REF-001");
        q1.AnswerDvType.Should().Be("List");
        q1.AnswerDvFormula.Should().Be("\"1,2,3,4\"");
        q1.AnswerDvOperator.Should().BeNull();
        q1.AnswerDvFormula2.Should().BeNull();
        q1.NumberFormatUnrecognized.Should().BeFalse();

        var q2 = questions[1];
        q2.RowNumber.Should().Be(4);
        q2.QuestionNumber.Should().Be("1.2");
        q2.QuestionText.Should().Be("Another question");
        q2.Answer.Should().BeNull();
        q2.NumberFormatUnrecognized.Should().BeFalse();
    }

    [Fact]
    public void Parse_BlankTextColumn_EmitsWarningAndSkips()
    {
        var config = MakeConfig();
        var rows = new List<ExcelRowStructure>
        {
            MakeRow(1, new() { ["D"] = TextCell("Chapter A") }),
            MakeRow(2, new() { ["D"] = TextCell("Section 1") }),
            MakeRow(3, new() { ["D"] = TextCell("") }),   // blank text → warning + skip
            MakeRow(4, new() { ["D"] = TextCell("1.1) Normal question") }),
        };

        var messages = new List<TaskMessage>();
        var questions = InternalClqQuestionParser.Parse(rows, config, messages);

        questions.Should().HaveCount(1);
        questions[0].RowNumber.Should().Be(4);

        messages.Should().HaveCount(1);
        messages[0].Severity.Should().Be(MessageSeverity.Warning);
        messages[0].Text.Should().Contain("3");
        messages[0].Text.Should().Contain("blank");
    }

    [Fact]
    public void Parse_AnswerCellWithDvList_CapturesDvFields()
    {
        var config = MakeConfig();
        var rows = new List<ExcelRowStructure>
        {
            MakeRow(1, new() { ["D"] = TextCell("Chapter A") }),
            MakeRow(2, new() { ["D"] = TextCell("Section 1") }),
            MakeRow(3, new()
            {
                ["D"] = TextCell("1.1) Question"),
                ["H"] = FullCell("3", dvType: "List", dvFormula: "\"1,2,3,4\"")
            }),
        };

        var messages = new List<TaskMessage>();
        var questions = InternalClqQuestionParser.Parse(rows, config, messages);

        questions.Should().HaveCount(1);
        var q = questions[0];
        q.AnswerDvType.Should().Be("List");
        q.AnswerDvFormula.Should().Be("\"1,2,3,4\"");
        q.AnswerDvOperator.Should().BeNull();
        q.AnswerDvFormula2.Should().BeNull();
    }

    [Fact]
    public void Parse_ThreeLevelPrefix_SetsNumberFormatUnrecognized()
    {
        var config = MakeConfig();
        var rows = new List<ExcelRowStructure>
        {
            MakeRow(1, new() { ["D"] = TextCell("Chapter A") }),
            MakeRow(2, new() { ["D"] = TextCell("Section 1") }),
            MakeRow(3, new() { ["D"] = TextCell("10.4.2) Three-level question") }),
        };

        var messages = new List<TaskMessage>();
        var questions = InternalClqQuestionParser.Parse(rows, config, messages);

        questions.Should().HaveCount(1);
        var q = questions[0];
        q.NumberFormatUnrecognized.Should().BeTrue();
        q.QuestionNumber.Should().Be("10.4");
        q.QuestionText.Should().Be(".2) Three-level question");
    }

    [Fact]
    public void Parse_RowOutsideSectionRange_IsSkipped()
    {
        var config = MakeConfig(); // section range: rows 3-8
        var rows = new List<ExcelRowStructure>
        {
            MakeRow(1, new() { ["D"] = TextCell("Chapter A") }),
            MakeRow(2, new() { ["D"] = TextCell("Section 1") }),
            MakeRow(3, new() { ["D"] = TextCell("1.1) In range") }),
            MakeRow(9, new() { ["D"] = TextCell("1.2) Out of range") }),
        };

        var messages = new List<TaskMessage>();
        var questions = InternalClqQuestionParser.Parse(rows, config, messages);

        questions.Should().HaveCount(1);
        questions[0].RowNumber.Should().Be(3);
    }

    [Fact]
    public void Parse_MultipleChaptersAndSections_AssignsCorrectly()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "D",
            GuidanceColumn = "E",
            PreviousAnswerColumn = "F",
            AnswerColumn = "H",
            StrengthsColumn = "I",
            WeaknessesColumn = "J",
            ProvidedByColumn = "M",
            XrefIdColumn = "N",
            SheetName = "CLQ",
            ChapterRows = [1, 10],
            SectionRows = ["2:3-5", "11:12-14"],
            AllowedAnswers = ["1", "2", "3", "4"],
            DeviationThreshold = 2
        };

        var rows = new List<ExcelRowStructure>
        {
            MakeRow(1,  new() { ["D"] = TextCell("Chapter One") }),
            MakeRow(2,  new() { ["D"] = TextCell("Section A") }),
            MakeRow(3,  new() { ["D"] = TextCell("1.1) Q in chapter one") }),
            MakeRow(10, new() { ["D"] = TextCell("Chapter Two") }),
            MakeRow(11, new() { ["D"] = TextCell("Section B") }),
            MakeRow(12, new() { ["D"] = TextCell("2.1) Q in chapter two") }),
        };

        var messages = new List<TaskMessage>();
        var questions = InternalClqQuestionParser.Parse(rows, config, messages);

        questions.Should().HaveCount(2);
        questions[0].ChapterName.Should().Be("Chapter One");
        questions[0].SectionName.Should().Be("Section A");
        questions[1].ChapterName.Should().Be("Chapter Two");
        questions[1].SectionName.Should().Be("Section B");
    }

    [Fact]
    public void Parse_EmptyRows_ReturnsEmptyList()
    {
        var config = MakeConfig();
        var messages = new List<TaskMessage>();
        var questions = InternalClqQuestionParser.Parse([], config, messages);
        questions.Should().BeEmpty();
        messages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ColumnLookupCaseInsensitive_MatchesUppercaseKeys()
    {
        // Config uses lowercase "d" — parser should uppercase before lookup
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "d",    // lowercase
            GuidanceColumn = "e",
            PreviousAnswerColumn = "f",
            AnswerColumn = "h",
            StrengthsColumn = "i",
            WeaknessesColumn = "j",
            ProvidedByColumn = "m",
            XrefIdColumn = "n",
            SheetName = "CLQ",
            ChapterRows = [1],
            SectionRows = ["2:3-5"],
            AllowedAnswers = ["1"],
            DeviationThreshold = 1
        };

        var rows = new List<ExcelRowStructure>
        {
            MakeRow(1, new() { ["D"] = TextCell("Chapter A") }),  // keys are uppercase
            MakeRow(2, new() { ["D"] = TextCell("Section 1") }),
            MakeRow(3, new() { ["D"] = TextCell("1.1) Question"), ["H"] = DvListCell("1", "\"1\"") }),
        };

        var messages = new List<TaskMessage>();
        var questions = InternalClqQuestionParser.Parse(rows, config, messages);

        questions.Should().HaveCount(1);
        questions[0].QuestionText.Should().Be("Question");
        questions[0].Answer.Should().Be("1");
    }
}
