using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;
using NSubstitute;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Parsing;

public sealed class DvPatcherTests
{
    private const string File = "response.xlsx";
    private const string Sheet = "CLQ";

    // Per-role updater: stamp the cell's DV type onto the question's single DV field.
    private static TestQuestion ApplyDv(TestQuestion q, ExcelCellStructure cell)
        => q with { Dv = cell.DataValidationType };

    private static TestQuestion Q(int row) => new(
        RowNumber: row, XrefId: $"X{row}",
        OriginalText: $"Question on topic {row}",
        QuestionText: $"Question on topic {row}",
        SectionName: "Section", QuestionNumber: null, ChapterName: "Chapter");

    private static IExcelStructureReader ReaderReturning(
        IReadOnlyDictionary<string, ExcelCellStructure> cells)
    {
        var reader = Substitute.For<IExcelStructureReader>();
        reader.ReadCells(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(cells);
        return reader;
    }

    [Fact]
    public void Patch_StampsDvFieldPerRow_AndMatchesByRowNumberNotOrder()
    {
        // Questions supplied out of row order; cells keyed by address.
        var questions = new List<TestQuestion> { Q(5), Q(3), Q(4) };
        var cells = new Dictionary<string, ExcelCellStructure>
        {
            ["H3"] = new(null, "List", "\"1,2,3\"", null),
            ["H4"] = new(null, "WholeNumber", null, null),
            ["H5"] = new(null, "Decimal", null, null),
        };
        var reader = ReaderReturning(cells);

        var result = DvPatcher.Patch(reader, File, Sheet, "H", questions, ApplyDv);

        // Order preserved (5,3,4); DV matched by RowNumber.
        result.Select(q => (q.RowNumber, q.Dv))
            .Should().Equal((5, "Decimal"), (3, "List"), (4, "WholeNumber"));
    }

    [Fact]
    public void Patch_BlankButDvdCell_IsCaptured()
    {
        // The lesson-71 case: a cell with no text value but a DV rule. ReadRows would have
        // omitted it; the address-driven re-read captures the DV.
        var questions = new List<TestQuestion> { Q(3) };
        var cells = new Dictionary<string, ExcelCellStructure>
        {
            ["H3"] = new(TextValue: null, DataValidationType: "List",
                         DataValidationFormula: "\"Yes,No,N/A\"", ConditionalFormattingOperator: null),
        };
        var reader = ReaderReturning(cells);

        var result = DvPatcher.Patch(reader, File, Sheet, "H", questions, ApplyDv);

        result.Should().ContainSingle().Which.Dv.Should().Be("List");
    }

    [Fact]
    public void Patch_CellWithoutDv_YieldsNullDvField()
    {
        var questions = new List<TestQuestion> { Q(3) };
        var cells = new Dictionary<string, ExcelCellStructure>
        {
            ["H3"] = new(TextValue: "a plain answer", DataValidationType: null,
                         DataValidationFormula: null, ConditionalFormattingOperator: null),
        };
        var reader = ReaderReturning(cells);

        var result = DvPatcher.Patch(reader, File, Sheet, "H", questions, ApplyDv);

        result.Should().ContainSingle().Which.Dv.Should().BeNull();
    }

    [Fact]
    public void Patch_AbsentAddress_YieldsNullDvField()
    {
        // ReadCells returns no entry for the address — defensive fallback ⇒ null DV.
        var questions = new List<TestQuestion> { Q(3) };
        var reader = ReaderReturning(new Dictionary<string, ExcelCellStructure>());

        var result = DvPatcher.Patch(reader, File, Sheet, "H", questions, ApplyDv);

        result.Should().ContainSingle().Which.Dv.Should().BeNull();
    }

    [Fact]
    public void Patch_RequestsRangeSpanningMinToMaxQuestionRow()
    {
        var questions = new List<TestQuestion> { Q(5), Q(3), Q(4) };
        var reader = ReaderReturning(new Dictionary<string, ExcelCellStructure>());

        DvPatcher.Patch(reader, File, Sheet, "h", questions, ApplyDv); // lower-case column ⇒ normalized

        reader.Received(1).ReadCells(File, Sheet,
            Arg.Is<IReadOnlyList<string>>(r => r.Count == 1 && r[0] == "H3:H5"));
    }

    [Fact]
    public void Patch_EmptyQuestions_ReturnsInputUnchanged_WithoutReading()
    {
        var reader = Substitute.For<IExcelStructureReader>();
        var empty = new List<TestQuestion>();

        var result = DvPatcher.Patch(reader, File, Sheet, "H", empty, ApplyDv);

        result.Should().BeSameAs(empty);
        reader.DidNotReceiveWithAnyArgs().ReadCells(default!, default!, default!);
    }
}
