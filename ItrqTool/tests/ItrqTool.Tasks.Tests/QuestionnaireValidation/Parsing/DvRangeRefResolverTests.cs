using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;
using NSubstitute;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Parsing;

public sealed class DvRangeRefResolverTests
{
    private const string FilePath      = "workbook.xlsx";
    private const string DvCellSheet   = "Questions";

    // Minimal record: only the fields Resolve reads/writes. T : class is the only constraint.
    private sealed record Q(
        string? DvType,
        string? DvFormula,
        IReadOnlyList<string>? CurrentList = null,
        IReadOnlyList<string>? Resolved = null);

    private static IExcelStructureReader ReaderReturningTextCells(
        string sheet, string a1Range, params string[] values)
    {
        var reader = Substitute.For<IExcelStructureReader>();
        var cells  = new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase);
        // one cell per value in sequence A1, A2, …
        for (int i = 0; i < values.Length; i++)
            cells[$"A{i + 1}"] = new ExcelCellStructure(
                values[i], null, null, null);

        reader.ReadCells(
                FilePath,
                Arg.Is<string>(s => s.Equals(sheet, StringComparison.Ordinal)),
                Arg.Is<IReadOnlyList<string>>(r => r.Count == 1 && r[0] == a1Range))
              .Returns(cells);
        return reader;
    }

    private static IReadOnlyList<Q> Resolve(
        IExcelStructureReader reader,
        IReadOnlyList<Q> questions) =>
        DvRangeRefResolver.Resolve(
            reader, FilePath, DvCellSheet, questions,
            q => q.DvType,
            q => q.DvFormula,
            q => q.CurrentList,
            (q, vals) => q with { Resolved = vals });

    // ── 1. Step-0 observed shape: qualified, $-absolute ──────────────────────────────────────

    [Fact]
    public void Resolve_QualifiedAbsoluteRangeRef_StampsValues()
    {
        // "Lists!$A$1:$A$2" is the exact formula ClosedXML emits (step-0 observed).
        // Parser: strip '$' → sheet="Lists", range="A1:A2".
        var reader = ReaderReturningTextCells("Lists", "A1:A2", "Yes", "No");
        var q = new Q(DvType: "List", DvFormula: "Lists!$A$1:$A$2");

        var result = Resolve(reader, [q]);

        result.Should().ContainSingle().Which.Resolved.Should()
            .Equal("Yes", "No");
    }

    // ── 2. Qualified without $ ────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_QualifiedBareRangeRef_StampsValues()
    {
        var reader = ReaderReturningTextCells("Sheet1", "A1:A3", "Low", "Med", "High");
        var q = new Q(DvType: "List", DvFormula: "Sheet1!A1:A3");

        var result = Resolve(reader, [q]);

        result.Should().ContainSingle().Which.Resolved.Should()
            .Equal("Low", "Med", "High");
    }

    // ── 3. Bare A1 range (no '!') — falls back to dvCellSheetName ────────────────────────────

    [Fact]
    public void Resolve_BareA1Range_UsesFallbackSheet()
    {
        var reader = ReaderReturningTextCells(DvCellSheet, "A1:A2", "Yes", "No");
        var q = new Q(DvType: "List", DvFormula: "A1:A2");

        var result = Resolve(reader, [q]);

        result.Should().ContainSingle().Which.Resolved.Should().Equal("Yes", "No");
    }

    // ── 4. Quoted sheet name ('My List'!$A$1:$A$2) ───────────────────────────────────────────

    [Fact]
    public void Resolve_QuotedSheetName_UnquotesAndResolves()
    {
        var reader = ReaderReturningTextCells("My List", "A1:A2", "Yes", "No");
        var q = new Q(DvType: "List", DvFormula: "'My List'!$A$1:$A$2");

        var result = Resolve(reader, [q]);

        result.Should().ContainSingle().Which.Resolved.Should().Equal("Yes", "No");
    }

    // ── 5. NamedRange — RESOLVES via ResolveDefinedNameValues (5b) ──────────────────────────────

    [Fact]
    public void Resolve_NamedRange_StampsResolvedValues()
    {
        // ClassifySource("MyDropdownList") → NamedRange; resolver returns ["Yes","No"].
        var reader = Substitute.For<IExcelStructureReader>();
        reader.ResolveDefinedNameValues(FilePath, DvCellSheet, "MyDropdownList")
              .Returns(new[] { "Yes", "No" });
        var q = new Q(DvType: "List", DvFormula: "MyDropdownList");

        var result = Resolve(reader, [q]);

        result.Should().ContainSingle().Which.Resolved.Should().Equal("Yes", "No");
        reader.DidNotReceiveWithAnyArgs().ReadCells(default!, default!, default!);
    }

    // ── 5a. NamedRange with leading '=' — '=' is stripped before the reader call ────────────────

    [Fact]
    public void Resolve_NamedRangeWithEqPrefix_StripsEqBeforeReaderCall()
    {
        // "=MyDropdownList" is what ClosedXML stores for the .List("=MyDropdownList") form.
        // ClassifySource strips the '=' before classifying → NamedRange.
        // The resolver must receive the BARE name "MyDropdownList", not "=MyDropdownList".
        var reader = Substitute.For<IExcelStructureReader>();
        reader.ResolveDefinedNameValues(FilePath, DvCellSheet, "MyDropdownList")
              .Returns(new[] { "Yes", "No" });
        var q = new Q(DvType: "List", DvFormula: "=MyDropdownList");

        var result = Resolve(reader, [q]);

        result.Should().ContainSingle().Which.Resolved.Should().Equal("Yes", "No");
        reader.Received(1).ResolveDefinedNameValues(
            Arg.Any<string>(), Arg.Any<string>(), "MyDropdownList");
    }

    // ── 5b. NamedRange absent — resolver returns null → question stays null (NotCheckable) ──────

    [Fact]
    public void Resolve_NamedRange_ResolverReturnsNull_QuestionUnchanged()
    {
        var reader = Substitute.For<IExcelStructureReader>();
        reader.ResolveDefinedNameValues(FilePath, DvCellSheet, "MyDropdownList")
              .Returns((IReadOnlyList<string>?)null);
        var q = new Q(DvType: "List", DvFormula: "MyDropdownList");

        var result = Resolve(reader, [q]);

        result.Should().ContainSingle().Which.Resolved.Should().BeNull();
    }

    // ── 6. Non-List DV type — UNCHANGED ──────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NonListDvType_LeavesQuestionUnchanged()
    {
        var reader = Substitute.For<IExcelStructureReader>();
        var q = new Q(DvType: "WholeNumber", DvFormula: "Lists!$A$1:$A$2");

        var result = Resolve(reader, [q]);

        result.Should().ContainSingle().Which.Resolved.Should().BeNull();
        reader.DidNotReceiveWithAnyArgs().ReadCells(default!, default!, default!);
    }

    // ── 7. Inline already resolved (currentList non-null) — not clobbered ────────────────────

    [Fact]
    public void Resolve_InlineAlreadyResolved_NotClobbered()
    {
        var reader = Substitute.For<IExcelStructureReader>();
        var existing = (IReadOnlyList<string>)new[] { "Yes", "No" };
        var q = new Q(DvType: "List", DvFormula: "Lists!$A$1:$A$2", CurrentList: existing);

        var result = Resolve(reader, [q]);

        result.Should().ContainSingle().Which.Resolved.Should().BeNull();
        reader.DidNotReceiveWithAnyArgs().ReadCells(default!, default!, default!);
    }

    // ── 8. Backing range with blank cells — blanks dropped ───────────────────────────────────

    [Fact]
    public void Resolve_BackingRangeWithBlanks_BlanksDropped()
    {
        // Backing cells: "Yes", "", "No" — the blank middle cell must be dropped.
        var reader = ReaderReturningTextCells("Lists", "A1:A3", "Yes", "", "No");
        var q = new Q(DvType: "List", DvFormula: "Lists!$A$1:$A$3");

        var result = Resolve(reader, [q]);

        result.Should().ContainSingle().Which.Resolved.Should().Equal("Yes", "No");
    }

    // ── 9. Backing range all-empty — stays null (NotCheckable, no false-positive) ─────────────

    [Fact]
    public void Resolve_AllEmptyBackingRange_StaysNull()
    {
        var reader = ReaderReturningTextCells("Lists", "A1:A2", "", "");
        var q = new Q(DvType: "List", DvFormula: "Lists!$A$1:$A$2");

        var result = Resolve(reader, [q]);

        result.Should().ContainSingle().Which.Resolved.Should().BeNull();
    }

    // ── 10. Case-insensitive DV type match ────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DvTypeLowercase_StampsValues()
    {
        var reader = ReaderReturningTextCells("Lists", "A1:A2", "Yes", "No");
        var q = new Q(DvType: "list", DvFormula: "Lists!$A$1:$A$2"); // lowercase

        var result = Resolve(reader, [q]);

        result.Should().ContainSingle().Which.Resolved.Should().Equal("Yes", "No");
    }

    // ── 11. Multiple questions — each stamped independently ───────────────────────────────────

    [Fact]
    public void Resolve_MultipleQuestions_EachStampedIndependently()
    {
        // Both questions have a range-ref DV pointing to the same backing range.
        var reader = ReaderReturningTextCells("Lists", "A1:A2", "Yes", "No");
        var q1 = new Q(DvType: "List", DvFormula: "Lists!$A$1:$A$2");
        var q2 = new Q(DvType: "List", DvFormula: "Lists!$A$1:$A$2");

        var result = Resolve(reader, [q1, q2]);

        result.Should().HaveCount(2);
        result[0].Resolved.Should().Equal("Yes", "No");
        result[1].Resolved.Should().Equal("Yes", "No");
    }

    // ── 12. Empty questions list — short-circuits without reading ─────────────────────────────

    [Fact]
    public void Resolve_EmptyQuestions_ReturnsEmptyWithoutReading()
    {
        var reader = Substitute.For<IExcelStructureReader>();

        var result = Resolve(reader, []);

        result.Should().BeEmpty();
        reader.DidNotReceiveWithAnyArgs().ReadCells(default!, default!, default!);
    }

    // ── ParseRangeRefFormula: direct parser tests ─────────────────────────────────────────────

    [Theory]
    [InlineData("Lists!$A$1:$A$2",      "Questions", "Lists",    "A1:A2")]
    [InlineData("Sheet1!A1:A3",          "Questions", "Sheet1",   "A1:A3")]
    [InlineData("A1:A2",                 "Questions", "Questions","A1:A2")]
    [InlineData("$A$1:$A$2",             "Questions", "Questions","A1:A2")]
    [InlineData("'My List'!$A$1:$A$2",   "Questions", "My List",  "A1:A2")]
    [InlineData("=Lists!$A$1:$A$2",      "Questions", "Lists",    "A1:A2")]
    [InlineData("'O''Brien'!$A$1:$A$2",  "Questions", "O'Brien",  "A1:A2")]  // BL-023: '' → '
    public void ParseRangeRefFormula_ReturnsExpectedSheetAndRange(
        string formula, string fallback, string expectedSheet, string expectedRange)
    {
        var (sheet, range) = DvRangeRefResolver.ParseRangeRefFormula(formula, fallback);
        sheet.Should().Be(expectedSheet);
        range.Should().Be(expectedRange);
    }
}
