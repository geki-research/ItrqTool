using System.Reflection;
using FluentAssertions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Tasks.RiskLevelQuestionInject;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;
using ItrqTool.Tasks.WorksheetStructure;

namespace ItrqTool.Tasks.Tests.RiskLevelQuestionInject;

/// <summary>
/// BL-053 P4b-R1 (additive, read-phase only): targeted unit tests proving
/// <see cref="RiskLevelQuestionInjectV01ToV02Task"/>'s enriched target-DV read
/// (<c>BuildTargetHLookup</c>) captures the FULL target answer-cell DV rule — type, operator,
/// both formulas, and resolved List vocabulary (inline and range-ref) — mirroring the
/// CellRangeInject-P3 read idiom. <c>BuildTargetHLookup</c> is a private instance method with
/// no public surface of its own (unlike CellRangeInjectTask's inline read, which is only
/// reachable through <c>ExecuteAsync</c>); reflection invokes it directly here so these tests
/// stay scoped to the read phase without adding any internal-visibility surface or touching the
/// task's public contract. Nothing here proves a MAPPER decision — that stays byte-equivalent
/// per <see cref="RlqInjectMapperTests"/>; R2 is what wires these captured fields into a decision.
/// </summary>
public sealed class RlqInjectTargetDvReadTests
{
    private static RlqV02Question Q(int row) => new(
        RowNumber: row,
        XrefId: "RLQ-1.1",
        OriginalText: "How is risk assessed?",
        QuestionText: "How is risk assessed?",
        SectionName: "Risk",
        QuestionNumber: "1.1",
        Guidance: null,
        RequestedType: null,
        PreviousAnswer: null,
        Answer: null,
        AnswerDvType: null,
        AnswerDvFormula: null,
        AnswerDvOperator: null,
        AnswerDvFormula2: null,
        MaterialChange: null,
        MaterialChangeDvType: null,
        MaterialChangeDvFormula: null,
        MaterialChangeDvOperator: null,
        MaterialChangeDvFormula2: null,
        ProvidedBy: null,
        ExplanationRows: [],
        HowExplanation: null);

    private static RlqV02Config Config(string answerColumn = "H") => new() { AnswerColumn = answerColumn };

    // Invokes the private BuildTargetHLookup(path, sheetName, config, questions) via reflection —
    // writer/mediator are unused by this method, so dummy substitutes suffice.
    private static IReadOnlyDictionary<int, RlqTargetDvHolder> InvokeBuildTargetHLookup(
        IExcelStructureReader reader, string path, string sheetName,
        RlqV02Config config, IReadOnlyList<RlqV02Question> questions)
    {
        var task = new RiskLevelQuestionInjectV01ToV02Task(
            reader, Substitute.For<IExcelTemplateWriter>(), Substitute.For<IWorksheetStructureMediator>());

        var method = typeof(RiskLevelQuestionInjectV01ToV02Task).GetMethod(
            "BuildTargetHLookup", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("BuildTargetHLookup must still exist as a private instance method");

        var result = method!.Invoke(task, [path, sheetName, config, questions]);
        return (IReadOnlyDictionary<int, RlqTargetDvHolder>)result!;
    }

    // ── 1. Value-typed target (WholeNumber, bounded) → Type/Operator/Formula/Formula2 captured ──

    [Fact]
    public void BuildTargetHLookup_ValueTypedTarget_CapturesOperatorAndBothFormulas()
    {
        var reader = Substitute.For<IExcelStructureReader>();
        reader.ReadCells("tpl.xlsx", "Sheet1", Arg.Any<IReadOnlyList<string>>())
            .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
            {
                ["H10"] = new(null, "WholeNumber", "1", null,
                    DataValidationOperator: "Between", DataValidationFormula2: "10"),
            });

        var result = InvokeBuildTargetHLookup(reader, "tpl.xlsx", "Sheet1", Config(), [Q(10)]);

        result.Should().ContainKey(10);
        var h = result[10];
        h.Type.Should().Be("WholeNumber");
        h.Operator.Should().Be("Between");
        h.Formula.Should().Be("1");
        h.Formula2.Should().Be("10");
        h.ListValues.Should().BeNull();
    }

    // ── 2. Inline-List target → ListValues resolved from the inline formula ──────────────────

    [Fact]
    public void BuildTargetHLookup_InlineListTarget_ResolvesListVocabulary()
    {
        var reader = Substitute.For<IExcelStructureReader>();
        reader.ReadCells("tpl.xlsx", "Sheet1", Arg.Any<IReadOnlyList<string>>())
            .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
            {
                ["H10"] = new(null, "List", "\"Yes,No\"", null),
            });

        var result = InvokeBuildTargetHLookup(reader, "tpl.xlsx", "Sheet1", Config(), [Q(10)]);

        result[10].Type.Should().Be("List");
        result[10].ListValues.Should().BeEquivalentTo(["Yes", "No"]);
    }

    // ── 3. Range-ref List target → ListValues resolved via DvRangeRefResolver against the same
    //       target workbook/sheet (mirrors CellRangeInjectTaskTests' equivalent range-ref case) ──

    [Fact]
    public void BuildTargetHLookup_RangeRefListTarget_ResolvesBackingCellValues()
    {
        var reader = Substitute.For<IExcelStructureReader>();
        reader.ReadCells("tpl.xlsx", "Sheet1", Arg.Is<IReadOnlyList<string>>(a => a.Contains("H10:H10")))
            .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
            {
                ["H10"] = new(null, "List", "K1:K2", null),
            });
        reader.ReadCells("tpl.xlsx", "Sheet1", Arg.Is<IReadOnlyList<string>>(a => a.Contains("K1:K2")))
            .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
            {
                ["K1"] = new("Alpha", null, null, null),
                ["K2"] = new("Beta", null, null, null),
            });

        var result = InvokeBuildTargetHLookup(reader, "tpl.xlsx", "Sheet1", Config(), [Q(10)]);

        result[10].Type.Should().Be("List");
        result[10].Formula.Should().Be("K1:K2");
        result[10].ListValues.Should().BeEquivalentTo(["Alpha", "Beta"]);
    }

    // ── 4. No questions → empty lookup, no reads ──────────────────────────────────────────────

    [Fact]
    public void BuildTargetHLookup_NoQuestions_ReturnsEmptyWithoutReadingCells()
    {
        var reader = Substitute.For<IExcelStructureReader>();

        var result = InvokeBuildTargetHLookup(reader, "tpl.xlsx", "Sheet1", Config(), []);

        result.Should().BeEmpty();
        reader.DidNotReceiveWithAnyArgs().ReadCells(default!, default!, default!);
    }
}
