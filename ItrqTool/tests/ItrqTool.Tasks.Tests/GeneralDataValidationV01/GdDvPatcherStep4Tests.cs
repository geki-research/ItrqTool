using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Tasks.GeneralDataValidationV01;
using NSubstitute;
using Xunit;

namespace ItrqTool.Tasks.Tests.GeneralDataValidationV01;

// Tests for GdDvPatcher.Patch step-4 (ResolveRangeAndNamedLists).
// Exercises the full Patch pipeline (steps 1–4) via a mocked IExcelStructureReader:
//   (a) inline-List H DV → stamped by steps 1–3, unchanged by step 4 (early-exit guard).
//   (b) RangeRef List H DV → null after step 3, resolved via ReadCells in step 4.
//   (c) NamedRange List L DV → null after step 3, resolved via ResolveDefinedNameValues in step 4.
public sealed class GdDvPatcherStep4Tests
{
    private const string FilePath  = "response.xlsx";
    private const string SheetName = "General Data";

    // ── Minimal config ───────────────────────────────────────────────────────────

    private static GdV01Config Config() => new()
    {
        QuestionNumberColumn = "C", TextColumn = "D", GuidanceColumn = "E",
        RequestedTypeColumn = "F", PreviousAnswerColumn = "G", AnswerColumn = "H",
        RequestedExplanationColumn = "I", PreviousExplanationColumn = "J",
        CurrentExplanationColumn = "K", MaterialChangeColumn = "L",
        ProvidedByColumn = "O", XrefIdColumn = "Q",
        SheetName = SheetName, SectionRows = ["3:4-9"], DeviationThreshold = 0.25,
    };

    // ── Answer/question builders ─────────────────────────────────────────────────

    private static GdAnswer AnswerAt(int anchorRow) =>
        new(AnswerId: $"A-{anchorRow:00}", AnchorRow: anchorRow,
            PreviousAnswer: null, Answer: null, MaterialChange: null,
            ProvidedBy: null, Explanations: []);

    private static GdV01Question Question(IReadOnlyList<GdAnswer> answers) =>
        new(RowNumber: answers[0].AnchorRow, XrefId: "Q1",
            OriginalText: "orig", QuestionText: "What?",
            SectionName: "G-ST", QuestionNumber: "1", Answers: answers);

    // ── Reader helper ────────────────────────────────────────────────────────────

    private static IExcelStructureReader BuildReader(
        IReadOnlyDictionary<string, ExcelCellStructure> hCells,
        IReadOnlyDictionary<string, ExcelCellStructure> lCells,
        (string sheet, string range, IReadOnlyDictionary<string, ExcelCellStructure> cells)? rangeRead = null,
        (string name, IReadOnlyList<string> values)? namedRange = null)
    {
        var reader = Substitute.For<IExcelStructureReader>();

        // H column scan (any one-element range starting with "H")
        reader.ReadCells(FilePath, SheetName,
                Arg.Is<IReadOnlyList<string>>(r => r.Count == 1 && r[0].StartsWith("H")))
              .Returns(hCells);

        // L column scan (any one-element range starting with "L")
        reader.ReadCells(FilePath, SheetName,
                Arg.Is<IReadOnlyList<string>>(r => r.Count == 1 && r[0].StartsWith("L")))
              .Returns(lCells);

        // Optional RangeRef resolution on a different sheet
        if (rangeRead is var (rs, rr, rc))
        {
            reader.ReadCells(FilePath, rs,
                    Arg.Is<IReadOnlyList<string>>(r => r.Count == 1 && r[0] == rr))
                  .Returns(rc);
        }

        // Optional NamedRange resolution
        if (namedRange is var (nn, nv))
            reader.ResolveDefinedNameValues(FilePath, SheetName, nn).Returns(nv);

        return reader;
    }

    // ── (a) Inline-List H DV — stays stamped after step 4 ───────────────────────

    [Fact]
    public void Patch_InlineListH_AlreadyStampedByStep3_UnchangedByStep4()
    {
        // Inline formula: "\"Yes,No\"" — classified as Inline, parsed to ["Yes","No"] by steps 1–3.
        var hCells = new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
        {
            ["H5"] = new("Yes", "List", "\"Yes,No\"", null),
        };
        var lCells = new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase);

        var reader = BuildReader(hCells, lCells);
        var q = Question([AnswerAt(5)]);

        var result = GdDvPatcher.Patch(reader, FilePath, SheetName, Config(), [q]);

        var answer = result.Should().ContainSingle().Subject.Answers.Should().ContainSingle().Subject;
        // Inline DV stamped in step 3.
        answer.AnswerDvType.Should().Be("List");
        answer.AnswerDvListValues.Should().Equal("Yes", "No");
        // Step 4 ReadCells / ResolveDefinedNameValues for the "Lists" sheet NOT called
        // (no range-ref formula on this answer).
        reader.DidNotReceive().ReadCells(FilePath, "Lists",
            Arg.Any<IReadOnlyList<string>>());
    }

    // ── (b) RangeRef List H DV — resolved in step 4 ─────────────────────────────

    [Fact]
    public void Patch_RangeRefListH_ResolvedByStep4()
    {
        // Formula "Lists!$A$1:$A$2" → classified as RangeRef → resolved from the "Lists" sheet.
        var hCells = new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
        {
            ["H10"] = new("Yes", "List", "Lists!$A$1:$A$2", null),
        };
        var lCells = new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase);
        var backingCells = new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
        {
            ["A1"] = new("Yes", null, null, null),
            ["A2"] = new("No",  null, null, null),
        };

        var reader = BuildReader(hCells, lCells,
            rangeRead: ("Lists", "A1:A2", backingCells));

        var q = Question([AnswerAt(10)]);
        var result = GdDvPatcher.Patch(reader, FilePath, SheetName, Config(), [q]);

        var answer = result.Should().ContainSingle().Subject.Answers.Should().ContainSingle().Subject;
        answer.AnswerDvType.Should().Be("List");
        answer.AnswerDvListValues.Should().Equal("Yes", "No");
        answer.MaterialChangeDvListValues.Should().BeNull();  // no L DV on this answer
    }

    // ── (c) NamedRange List L DV — resolved in step 4 ───────────────────────────

    [Fact]
    public void Patch_NamedRangeListL_ResolvedByStep4()
    {
        // Formula "=YesNoOptions" → classified as NamedRange → resolved via ResolveDefinedNameValues.
        var hCells = new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase);
        var lCells = new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
        {
            ["L15"] = new(null, "List", "=YesNoOptions", null),
        };

        var reader = BuildReader(hCells, lCells,
            namedRange: ("YesNoOptions", new List<string> { "Y", "N", "N/A" }));

        var q = Question([AnswerAt(15)]);
        var result = GdDvPatcher.Patch(reader, FilePath, SheetName, Config(), [q]);

        var answer = result.Should().ContainSingle().Subject.Answers.Should().ContainSingle().Subject;
        answer.AnswerDvListValues.Should().BeNull();           // no H DV
        answer.MaterialChangeDvType.Should().Be("List");
        answer.MaterialChangeDvListValues.Should().Equal("Y", "N", "N/A");
    }

    // ── All three scenarios combined ─────────────────────────────────────────────

    [Fact]
    public void Patch_AllThreeDvKinds_EachAnswerIndependentlyResolved()
    {
        // Answer at row 5: inline H DV ("Yes,No") → stamped by steps 1–3.
        // Answer at row 10: RangeRef H DV ("Lists!$A$1:$A$2") → resolved in step 4.
        // Answer at row 15: NamedRange L DV ("=YesNoOptions") → resolved in step 4.
        var hCells = new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
        {
            ["H5"]  = new("Yes", "List", "\"Yes,No\"",     null),   // inline
            ["H10"] = new("Yes", "List", "Lists!$A$1:$A$2", null),  // range-ref
        };
        var lCells = new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
        {
            ["L15"] = new(null, "List", "=YesNoOptions", null),      // named-range
        };
        var backingCells = new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
        {
            ["A1"] = new("Yes", null, null, null),
            ["A2"] = new("No",  null, null, null),
        };

        var reader = BuildReader(hCells, lCells,
            rangeRead:  ("Lists", "A1:A2", backingCells),
            namedRange: ("YesNoOptions", new List<string> { "Y", "N" }));

        var q = Question([AnswerAt(5), AnswerAt(10), AnswerAt(15)]);
        var result = GdDvPatcher.Patch(reader, FilePath, SheetName, Config(), [q]);

        var answers = result.Should().ContainSingle().Subject.Answers;
        answers.Should().HaveCount(3);

        // (a) inline: already stamped by steps 1–3, unchanged.
        var a5 = answers.Single(a => a.AnchorRow == 5);
        a5.AnswerDvListValues.Should().Equal("Yes", "No");

        // (b) range-ref: resolved in step 4.
        var a10 = answers.Single(a => a.AnchorRow == 10);
        a10.AnswerDvListValues.Should().Equal("Yes", "No");

        // (c) named-range: resolved in step 4 on L.
        var a15 = answers.Single(a => a.AnchorRow == 15);
        a15.AnswerDvListValues.Should().BeNull();
        a15.MaterialChangeDvListValues.Should().Equal("Y", "N");
    }
}
