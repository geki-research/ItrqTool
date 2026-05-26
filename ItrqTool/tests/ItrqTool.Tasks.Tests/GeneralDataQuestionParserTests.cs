using FluentAssertions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Tasks.GeneralDataDiff;

namespace ItrqTool.Tasks.Tests;

public sealed class GeneralDataQuestionParserTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ExcelRowStructure Row(int rowNum, params (string col, string? text)[] cells)
    {
        var dict = cells.ToDictionary(
            c => c.col,
            c => new ExcelCellStructure(c.text, null, null, null));
        return new ExcelRowStructure(rowNum, dict);
    }

    private static ExcelRowStructure RowWithDv(int rowNum, string col, string? text, string? dvType, string? dvFormula, string? cfOp)
    {
        var dict = new Dictionary<string, ExcelCellStructure>
        {
            [col] = new ExcelCellStructure(text, dvType, dvFormula, cfOp)
        };
        return new ExcelRowStructure(rowNum, dict);
    }

    private static ExcelRowStructure RowFull(int rowNum, Dictionary<string, ExcelCellStructure> cells)
        => new ExcelRowStructure(rowNum, cells);

    // ── Single-row single-question cases ──────────────────────────────────────

    [Fact]
    public void Parse_SingleRowQuestion_ReturnsOneQuestionWithCorrectFields()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            RowFull(14, new Dictionary<string, ExcelCellStructure>
            {
                ["B"] = new("5.a", null, null, null),
                ["C"] = new("How many FTEs?", null, null, null),
                ["D"] = new("<# of Group FTEs>", "Decimal", null, null)
            })
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().HaveCount(1);
        var q = result[0];
        q.SectionName.Should().Be("Section A");
        q.QuestionText.Should().Be("How many FTEs?");
        q.QuestionNumber.Should().Be("5.a");
        q.RowNumber.Should().Be(14);
        q.RowNumberLabels.Should().Equal("5.a");
        q.AnswerCells.Should().HaveCount(1);
        q.AnswerCells[0].RowOffset.Should().Be(0);
        q.AnswerCells[0].Column.Should().Be("D");
        q.AnswerCells[0].Text.Should().Be("<# of Group FTEs>");
        q.AnswerCells[0].DvType.Should().Be("Decimal");
        q.ExplanationCells.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SingleRowQuestionWithExplanation_PopulatesExplanationCell()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            RowFull(14, new Dictionary<string, ExcelCellStructure>
            {
                ["B"] = new("5.a", null, null, null),
                ["C"] = new("How many FTEs?", null, null, null),
                ["D"] = new("<# of Group FTEs>", "Decimal", null, null),
                ["G"] = new("Explain methodology", null, null, null)
            })
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().HaveCount(1);
        result[0].ExplanationCells.Should().HaveCount(1);
        result[0].ExplanationCells[0].RowOffset.Should().Be(0);
        result[0].ExplanationCells[0].Text.Should().Be("Explain methodology");
    }

    // ── Multi-row sparse case ─────────────────────────────────────────────────

    [Fact]
    public void Parse_MultiRowQuestionWithSparseAnswers_FlattenedToAnswerCellsList()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            RowFull(14, new Dictionary<string, ExcelCellStructure>
            {
                ["B"] = new("5.a", null, null, null),
                ["C"] = new("Annual breakdown", null, null, null),
                ["D"] = new("<Year>", null, null, null)
            }),
            RowFull(15, new Dictionary<string, ExcelCellStructure>
            {
                ["B"] = new("5.b", null, null, null),
                ["D"] = new("<Amount €>", null, null, null),
                ["E"] = new("<Description>", null, null, null)
            }),
            RowFull(16, new Dictionary<string, ExcelCellStructure>
            {
                ["B"] = new("5.c", null, null, null),
                ["D"] = new("<Cloud spend>", null, null, null),
                ["E"] = new("<On-prem spend>", null, null, null)
            })
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(3)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().HaveCount(1);
        var q = result[0];
        q.RowNumberLabels.Should().Equal("5.a", "5.b", "5.c");
        q.AnswerCells.Should().HaveCount(5);
        q.AnswerCells[0].Should().Be(new GeneralDataAnswerCell(0, "D", "<Year>", null, null, null));
        q.AnswerCells[1].Should().Be(new GeneralDataAnswerCell(1, "D", "<Amount €>", null, null, null));
        q.AnswerCells[2].Should().Be(new GeneralDataAnswerCell(1, "E", "<Description>", null, null, null));
        q.AnswerCells[3].Should().Be(new GeneralDataAnswerCell(2, "D", "<Cloud spend>", null, null, null));
        q.AnswerCells[4].Should().Be(new GeneralDataAnswerCell(2, "E", "<On-prem spend>", null, null, null));
        q.ExplanationCells.Should().BeEmpty();
    }

    // ── DV/CF metadata propagation ────────────────────────────────────────────

    [Fact]
    public void Parse_AnswerCellWithDvAndCf_PropagatesToAnswerCell()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            RowFull(14, new Dictionary<string, ExcelCellStructure>
            {
                ["C"] = new("Question text", null, null, null),
                ["D"] = new("<Answer>", "List", "\"Yes,No\"", "notBlank")
            })
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().HaveCount(1);
        var cell = result[0].AnswerCells[0];
        cell.DvType.Should().Be("List");
        cell.DvFormula.Should().Be("\"Yes,No\"");
        cell.CfOperator.Should().Be("notBlank");
    }

    [Fact]
    public void Parse_ExplanationCellWithDv_PropagatesToExplanationCell()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            RowFull(14, new Dictionary<string, ExcelCellStructure>
            {
                ["C"] = new("Question text", null, null, null),
                ["G"] = new("Explain", "TextLength", null, null)
            })
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().HaveCount(1);
        result[0].ExplanationCells[0].DvType.Should().Be("TextLength");
    }

    // ── Multiple questions / multiple sections ────────────────────────────────

    [Fact]
    public void Parse_TwoQuestionsInOneSection_ReturnsBothInOrder()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            Row(14, ("C", "Question one")),
            Row(15, ("C", "Question two continuation")),
            Row(16, ("C", "Question two second row"))
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1), 15(2)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().HaveCount(2);
        result[0].RowNumber.Should().Be(14);
        result[1].RowNumber.Should().Be(15);
    }

    [Fact]
    public void Parse_TwoSections_ReturnsAllQuestionsFromBoth()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            Row(14, ("C", "Question A1")),
            Row(21, ("C", "Section B")),
            Row(22, ("C", "Question B1"))
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1)", "21:22(1)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().HaveCount(2);
        result[0].SectionName.Should().Be("Section A");
        result[1].SectionName.Should().Be("Section B");
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_QuestionWithMissingFirstRow_SkipsQuestion()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A"))
            // row 14 intentionally absent
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_QuestionWithEmptyTextCell_SkipsQuestion()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            Row(14, ("C", ""))
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_QuestionWithMissingTextCell_SkipsQuestion()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            Row(14, ("B", "1.1"))  // no "C" cell at all
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_QuestionWithWhitespaceOnlyText_SkipsQuestion()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            Row(14, ("C", "   "))
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_QuestionWithMissingContinuationRow_StillProducesQuestionWithBlankSlot()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            Row(14, ("B", "5.a"), ("C", "Spanning question"), ("D", "<D14>")),
            // row 15 missing
            Row(16, ("B", "5.c"), ("D", "<D16>"))
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(3)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().HaveCount(1);
        var q = result[0];
        q.RowNumberLabels.Should().HaveCount(3);
        q.RowNumberLabels[1].Should().Be("");   // row 15 missing → blank slot
        q.AnswerCells.Should().HaveCount(2);    // D14 and D16 only
        q.AnswerCells[0].RowOffset.Should().Be(0);
        q.AnswerCells[1].RowOffset.Should().Be(2);
    }

    [Fact]
    public void Parse_QuestionWithMissingNumberCell_PopulatesEmptyLabel()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            Row(14, ("C", "Question text"))  // no "B" cell
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().HaveCount(1);
        result[0].RowNumberLabels[0].Should().Be("");
        result[0].QuestionNumber.Should().BeNull();
    }

    // ── Cross-section validation ──────────────────────────────────────────────

    [Fact]
    public void Parse_QuestionsOverlappingNextSection_Throws()
    {
        // Question spans rows 14-23, but next section starts at row 20
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            Row(14, ("C", "Q1"))
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(10)", "20:21(1)"] };

        var act = () => GeneralDataQuestionParser.Parse(rows, cfg);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_NonIncreasingSectionRows_Throws()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            Row(14, ("C", "Q1")),
            Row(20, ("C", "Section B")),
            Row(21, ("C", "Q2"))
        };
        var cfg = new GeneralDataConfig { SectionRows = ["20:21(1)", "13:14(1)"] };

        var act = () => GeneralDataQuestionParser.Parse(rows, cfg);

        act.Should().Throw<FormatException>();
    }

    // ── Cell-inclusion rule ───────────────────────────────────────────────────

    [Fact]
    public void Parse_AnswerCellWithDvButEmptyText_IsExcluded()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            RowFull(14, new Dictionary<string, ExcelCellStructure>
            {
                ["C"] = new("Question text", null, null, null),
                ["D"] = new("", "Decimal", null, null)  // empty text, has DV
            })
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().HaveCount(1);
        result[0].AnswerCells.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ExplanationCellWithDvButEmptyText_IsExcluded()
    {
        var rows = new List<ExcelRowStructure>
        {
            Row(13, ("C", "Section A")),
            RowFull(14, new Dictionary<string, ExcelCellStructure>
            {
                ["C"] = new("Question text", null, null, null),
                ["G"] = new("", "TextLength", null, null)  // empty text, has DV
            })
        };
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1)"] };

        var result = GeneralDataQuestionParser.Parse(rows, cfg);

        result.Should().HaveCount(1);
        result[0].ExplanationCells.Should().BeEmpty();
    }
}
