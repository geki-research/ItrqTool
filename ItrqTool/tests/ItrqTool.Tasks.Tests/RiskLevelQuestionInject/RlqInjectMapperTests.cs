using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.RiskLevelQuestionInject;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;
using ItrqTool.Tasks.Shared;
using Xunit;

namespace ItrqTool.Tasks.Tests.RiskLevelQuestionInject;

/// <summary>
/// Pure unit tests for <see cref="RlqInjectMapper"/> — no Excel, no mocks. Each test builds
/// the question records, a <see cref="CrossFormatMatch{TCur,TPrev}"/> and the two H lookups
/// directly, then asserts the emitted (cells, messages) — including the H→G entry's
/// <see cref="CellWriteEntry.TypedValue"/> on the typed arms.
/// </summary>
public sealed class RlqInjectMapperTests
{
    // ── builders ──────────────────────────────────────────────────────────────

    // The mapper only reads these three columns from the config; the rest stay defaulted.
    private static RlqV02Config Config(string g = "G", string j = "J", string p = "P") => new()
    {
        PreviousAnswerColumn = g,
        PreviousExplanationColumn = j,
        ProvidedByColumn = p,
    };

    private static RlqV01Question Prev(
        int row = 100,
        string? answer = "3",
        string? providedBy = null,
        IReadOnlyList<RlqExplanationRow>? expl = null) => new(
        RowNumber: row,
        XrefId: "RLQ-1.1",
        OriginalText: "How is risk assessed?",
        QuestionText: "How is risk assessed?",
        SectionName: "Risk",
        QuestionNumber: "1.1",
        Guidance: null,
        RequestedType: null,
        PreviousAnswer: null,
        Answer: answer,
        AnswerDvType: null,
        AnswerDvFormula: null,
        AnswerDvOperator: null,
        AnswerDvFormula2: null,
        MaterialChange: null,
        MaterialChangeDvType: null,
        MaterialChangeDvFormula: null,
        MaterialChangeDvOperator: null,
        MaterialChangeDvFormula2: null,
        ProvidedBy: providedBy,
        ExplanationRows: expl ?? []);

    private static RlqV02Question Cur(
        int row = 10,
        string? xref = "RLQ-1.1",
        IReadOnlyList<RlqExplanationRow>? expl = null) => new(
        RowNumber: row,
        XrefId: xref,
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
        ExplanationRows: expl ?? [],
        HowExplanation: null);

    private static RlqExplanationRow Expl(string? current, int row)
        => new(Requested: null, Previous: null, Current: current, RowNumber: row);

    private static CrossFormatMatch<RlqV02Question, RlqV01Question> Agree(
        RlqV02Question c, RlqV01Question p)
        => new(c, CrossYearOutcome.Agree, p, p, p, 0.95);

    private static CrossFormatMatch<RlqV02Question, RlqV01Question> Ambiguous(
        RlqV02Question c, CrossYearOutcome outcome)
        => new(c, outcome, null, null, null, null);

    // Builds a target-DV lookup entry with only Type set — the mapper's decision logic (R1)
    // reads Type only, so tests that only need to drive that decision can omit the other fields.
    private static TargetDvInfo TgtDv(string? type)
        => new(type, Operator: null, Formula: null, Formula2: null, ListValues: null);

    private static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) MapOne(
        CrossFormatMatch<RlqV02Question, RlqV01Question> match,
        RlqV02Config config,
        IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)>? sourceH = null,
        IReadOnlyDictionary<int, TargetDvInfo>? targetH = null)
    {
        var result = new CrossFormatAlignmentResult<RlqV02Question, RlqV01Question>([match], []);
        return RlqInjectMapper.Map(
            result, config,
            sourceH ?? new Dictionary<int, (string?, object?, string?)>(),
            targetH ?? new Dictionary<int, TargetDvInfo>());
    }

    private static CellWriteEntry? Cell(IReadOnlyList<CellWriteEntry> cells, int row, string column)
        => cells.SingleOrDefault(e => e.Row == row && e.Column == column);

    // ── BLG-0079: non-identical Agree → exactly one Warning ───────────────────
    //
    // Note the default Cur()/Prev() builders share OriginalText ("How is risk assessed?"), so every
    // pre-existing test in this file drives an IDENTICAL-text Agree and stays silent, untouched.
    // These tests supply a drifted text explicitly.

    private static CrossFormatMatch<RlqV02Question, RlqV01Question> AgreeWithTexts(
        string currentText, string previousText, double? score, int currentRow = 10)
    {
        var c = Cur(row: currentRow) with { OriginalText = currentText, QuestionText = currentText };
        var p = Prev() with { OriginalText = previousText, QuestionText = previousText };
        return new(c, CrossYearOutcome.Agree, p, p, p, score);
    }

    [Fact]
    public void Agree_IdenticalText_EmitsNoWarning()
    {
        var (_, messages) = MapOne(Agree(Cur(), Prev()), Config());

        messages.Should().BeEmpty("an unchanged question is the ordinary case and stays silent");
    }

    [Fact]
    public void Agree_ChangedText_EmitsExactlyOneWarning_WithBothTextsBothRowsAndScore()
    {
        var (_, messages) = MapOne(
            AgreeWithTexts("How is risk assessed in 2025?", "How is risk assessed in 2024?",
                           score: 0.9655, currentRow: 42),
            Config());

        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Warning);

        var text = messages[0].Text;
        text.Should().StartWith("Row 42 (xref RLQ-1.1)", "RLQ keeps its own Row-first addressing");
        text.Should().Contain("100", "the previous row is named");
        text.Should().Contain("0.9655", "the raw score, invariant-formatted");
        text.Should().Contain("How is risk assessed in 2025?");
        text.Should().Contain("How is risk assessed in 2024?");
        text.Should().Contain("question text changed");
        text.Should().Contain("carried forward");
    }

    // Pins the COMPLETE sentence, so the shared formatter cannot silently reshape RLQ's message
    // and so the report's example rendering is a verified string rather than a reconstruction.
    [Fact]
    public void Agree_ChangedText_ExactMessageFormat()
    {
        var (_, messages) = MapOne(
            AgreeWithTexts("How is risk assessed in 2025?", "How is risk assessed in 2024?",
                           score: 0.9655, currentRow: 42),
            Config());

        messages.Should().ContainSingle().Which.Text.Should().Be(
            "Row 42 (xref RLQ-1.1): question text changed since the previous year " +
            "(similarity 0.9655) — treated as the same question; previous values carried forward. " +
            "Previous (row 100): \"How is risk assessed in 2024?\". " +
            "Current: \"How is risk assessed in 2025?\".");
    }

    // Case-only drift scores 1.0 but is a real change, so it must still warn.
    [Fact]
    public void Agree_CaseOnlyDrift_StillWarns()
    {
        var (_, messages) = MapOne(
            AgreeWithTexts("HOW IS RISK ASSESSED?", "how is risk assessed?", score: 1.0),
            Config());

        messages.Should().ContainSingle().Which.Severity.Should().Be(MessageSeverity.Warning);
    }

    [Fact]
    public void Agree_ChangedText_NullScore_RendersUnknownNotZero()
    {
        var (_, messages) = MapOne(
            AgreeWithTexts("New wording", "Old wording", score: null), Config());

        var text = messages.Should().ContainSingle().Subject.Text;
        text.Should().Contain("unknown");
        text.Should().NotContain("0.0000");
    }

    // The Warning is an annunciation, not a veto: the same cells are still written.
    [Fact]
    public void Agree_ChangedText_StillWritesTheSameCells()
    {
        var identical = MapOne(Agree(Cur(), Prev(providedBy: "OU1")), Config());
        var drifted   = MapOne(
            new CrossFormatMatch<RlqV02Question, RlqV01Question>(
                Cur() with { OriginalText = "New wording", QuestionText = "New wording" },
                CrossYearOutcome.Agree,
                Prev(providedBy: "OU1") with { OriginalText = "Old wording", QuestionText = "Old wording" },
                null, null, 0.72),
            Config());

        drifted.cells.Should().BeEquivalentTo(identical.cells,
            "the drift Warning changes messages only — never which cells are written");
        drifted.messages.Should().ContainSingle().Which.Severity.Should().Be(MessageSeverity.Warning);
    }

    // Exactly ONE per question, even when the question spans several explanation rows.
    [Fact]
    public void Agree_ChangedText_MultiRowQuestion_StillEmitsExactlyOneWarning()
    {
        var c = Cur(expl: [Expl(null, 10), Expl(null, 11), Expl(null, 12)])
            with { OriginalText = "New wording", QuestionText = "New wording" };
        var p = Prev(expl: [Expl("a", 100), Expl("b", 101), Expl("c", 102)])
            with { OriginalText = "Old wording", QuestionText = "Old wording" };

        var (_, messages) = MapOne(
            new CrossFormatMatch<RlqV02Question, RlqV01Question>(
                c, CrossYearOutcome.Agree, p, p, p, 0.72),
            Config());

        messages.Where(m => m.Text.Contains("question text changed"))
            .Should().HaveCount(1, "the Warning is per question, not per row");
    }

    // ── 1. H→G equal category → native, no message ────────────────────────────

    [Fact]
    public void Hg_EqualType_WritesNativeNoMessage()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("WholeNumber", 3.0, "3") };
        var tgt = new Dictionary<int, TargetDvInfo> { [10] = TgtDv("WholeNumber") };

        var (cells, messages) = MapOne(Agree(Cur(), Prev()), Config(), src, tgt);

        var g = Cell(cells, 10, "G");
        g.Should().NotBeNull();
        g!.TypedValue.Should().Be(3.0);
        g.TypedValue.Should().BeOfType<double>();
        messages.Should().BeEmpty();
    }

    // ── 2. H→G widen (WholeNumber → Decimal) → guard Inject, native + Info ────

    [Fact]
    public void Hg_WholeToDecimal_WritesNativePlusInfo()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("WholeNumber", 3.0, "3") };
        var tgt = new Dictionary<int, TargetDvInfo> { [10] = TgtDv("Decimal") };

        var (cells, messages) = MapOne(Agree(Cur(), Prev()), Config(), src, tgt);

        Cell(cells, 10, "G")!.TypedValue.Should().Be(3.0);
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Info);
        messages[0].Text.Should().Contain("widened");
    }

    // ── 3. H→G narrow (Decimal → WholeNumber) → guard NotConformant → skip + Error ──

    [Fact]
    public void Hg_DecimalToWhole_SkipsWithError()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("Decimal", 3.5, "3.5") };
        var tgt = new Dictionary<int, TargetDvInfo> { [10] = TgtDv("WholeNumber") };

        var (cells, messages) = MapOne(Agree(Cur(), Prev()), Config(), src, tgt);

        Cell(cells, 10, "G").Should().BeNull(); // no longer written — guard finds it non-conformant
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Error);
        messages[0].Text.Should().Contain("does not conform");
    }

    // ── 4. H→G mismatch → guard NotConformant → skip + Error, continue (K→J / O→P still run) ──

    [Fact]
    public void Hg_IncompatibleTypes_EmitsErrorSkipsCellContinues()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("List", "Yes", "Yes") };
        var tgt = new Dictionary<int, TargetDvInfo> { [10] = TgtDv("WholeNumber") };

        var c = Cur(row: 10, expl: [Expl("expl-A", 11)]);
        var p = Prev(row: 100, answer: "Yes", providedBy: "Bob", expl: [Expl("expl-A", 101)]);

        var (cells, messages) = MapOne(Agree(c, p), Config(), src, tgt);

        Cell(cells, 10, "G").Should().BeNull(); // G skipped
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Error);
        messages[0].Text.Should().Contain("does not conform");

        // The SAME match's other writes still happen (continue, not abort).
        Cell(cells, 11, "J")!.Value.Should().Be("expl-A");
        Cell(cells, 10, "P")!.Value.Should().Be("Bob");
    }

    // ── 5. H→G blank source → omit cell, no message ───────────────────────────

    [Fact]
    public void Hg_BlankSource_OmitsCell()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("WholeNumber", null, null) };
        var tgt = new Dictionary<int, TargetDvInfo> { [10] = TgtDv("WholeNumber") };

        var (cells, messages) = MapOne(Agree(Cur(), Prev(providedBy: null)), Config(), src, tgt);

        Cell(cells, 10, "G").Should().BeNull();
        messages.Should().BeEmpty();
    }

    // ── 5b. H→G List target, non-member source → guard NotConformant → skip + Error ──
    // (BL-053 P4b-R2: List-membership enforcement now live.)

    [Fact]
    public void Hg_ListTargetNonMemberSource_SkipsWithError()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = (null, "Maybe", "Maybe") };
        var tgt = new Dictionary<int, TargetDvInfo>
        {
            [10] = new("List", Operator: null, Formula: null, Formula2: null, ListValues: ["Yes", "No"])
        };

        var (cells, messages) = MapOne(Agree(Cur(), Prev(answer: "Maybe")), Config(), src, tgt);

        Cell(cells, 10, "G").Should().BeNull();
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Error);
        messages[0].Text.Should().Contain("does not conform");
    }

    // ── 5c. H→G numeric target, source violates the operator bound → skip + Error ──
    // (BL-053 P4b-R2: operator/bound enforcement now live.)

    [Fact]
    public void Hg_NumericOperatorBoundViolation_SkipsWithError()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("WholeNumber", 15.0, "15") };
        var tgt = new Dictionary<int, TargetDvInfo>
        {
            [10] = new("WholeNumber", Operator: "Between", Formula: "1", Formula2: "10", ListValues: null)
        };

        var (cells, messages) = MapOne(Agree(Cur(), Prev(answer: "15")), Config(), src, tgt);

        Cell(cells, 10, "G").Should().BeNull();
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Error);
        messages[0].Text.Should().Contain("does not conform");
    }

    // ── 5d. H→G comma-rendered decimal source, native in bound → injects (BL-058) ──

    [Fact]
    public void Hg_CommaDecimalSource_NativeInBound_Injects()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("Decimal", 9.1, "9,1") };
        var tgt = new Dictionary<int, TargetDvInfo>
        {
            [10] = new("Decimal", Operator: "Between", Formula: "0", Formula2: "100", ListValues: null)
        };

        var (cells, messages) = MapOne(Agree(Cur(), Prev(answer: "9,1")), Config(), src, tgt);

        var g = Cell(cells, 10, "G");
        g.Should().NotBeNull();
        g!.TypedValue.Should().Be(9.1);
        messages.Should().BeEmpty();
    }

    // ── 6. K→J position-aligned → one write per overlapping row ───────────────

    [Fact]
    public void KtoJ_PositionAligned_WritesPerRow()
    {
        var c = Cur(row: 10, expl: [Expl(null, 11), Expl(null, 12), Expl(null, 13)]);
        var p = Prev(row: 100, expl: [Expl("s1", 101), Expl("s2", 102), Expl("s3", 103)]);

        var (cells, messages) = MapOne(Agree(c, p), Config());

        Cell(cells, 11, "J")!.Value.Should().Be("s1");
        Cell(cells, 12, "J")!.Value.Should().Be("s2");
        Cell(cells, 13, "J")!.Value.Should().Be("s3");
        messages.Should().BeEmpty();
    }

    // ── 7. K→J row-count mismatch → overlap written + ONE Warning ─────────────

    [Fact]
    public void KtoJ_RowCountMismatch_WritesOverlapPlusOneWarning()
    {
        var c = Cur(row: 10, expl: [Expl(null, 11), Expl(null, 12)]);
        var p = Prev(row: 100, expl: [Expl("s1", 101), Expl("s2", 102), Expl("s3", 103)]);

        var (cells, messages) = MapOne(Agree(c, p), Config());

        Cell(cells, 11, "J")!.Value.Should().Be("s1");
        Cell(cells, 12, "J")!.Value.Should().Be("s2");
        cells.Should().NotContain(e => e.Column == "J" && e.Row == 13);
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Warning);
        messages[0].Text.Should().Contain("explanation row count mismatch");
    }

    // ── 8. K→J blank source row → that row omitted ────────────────────────────

    [Fact]
    public void KtoJ_BlankSourceRow_OmitsThatRow()
    {
        var c = Cur(row: 10, expl: [Expl(null, 11), Expl(null, 12), Expl(null, 13)]);
        var p = Prev(row: 100, expl: [Expl("s1", 101), Expl("   ", 102), Expl("s3", 103)]);

        var (cells, messages) = MapOne(Agree(c, p), Config());

        Cell(cells, 11, "J")!.Value.Should().Be("s1");
        Cell(cells, 12, "J").Should().BeNull();
        Cell(cells, 13, "J")!.Value.Should().Be("s3");
        messages.Should().BeEmpty();
    }

    // ── 9. O→P provided-by text; blank → omitted ──────────────────────────────

    [Fact]
    public void OtoP_ProvidedBy_WritesText()
    {
        var (cells, _) = MapOne(Agree(Cur(), Prev(providedBy: "Carol")), Config());
        var p = Cell(cells, 10, "P");
        p.Should().NotBeNull();
        p!.Value.Should().Be("Carol");
        p.TypedValue.Should().BeNull();

        var (blankCells, _) = MapOne(Agree(Cur(), Prev(providedBy: "   ")), Config());
        Cell(blankCells, 10, "P").Should().BeNull();
    }

    // ── 10. Ambiguous outcomes → one Warning, no cells ────────────────────────

    [Fact]
    public void Ambiguous_EmitsOneWarningNoCells()
    {
        var c = Cur(row: 42, xref: "RLQ-3.1");

        foreach (var outcome in new[]
                 {
                     CrossYearOutcome.XrefIdConflict,
                     CrossYearOutcome.NewXrefIdWithLookalike,
                     CrossYearOutcome.SameXrefIdTextDiverged,
                 })
        {
            var (cells, messages) = MapOne(Ambiguous(c, outcome), Config());

            cells.Should().BeEmpty();
            messages.Should().ContainSingle();
            messages[0].Severity.Should().Be(MessageSeverity.Warning);
            messages[0].Text.Should().Contain("42");
            messages[0].Text.Should().Contain("RLQ-3.1");
            messages[0].Text.Should().Contain("ambiguous previous match");
        }
    }

    // ── 11. New / structural → nothing ────────────────────────────────────────

    [Fact]
    public void Neither_EmitsNothing()
    {
        foreach (var outcome in new[]
                 {
                     CrossYearOutcome.Neither,
                     CrossYearOutcome.NotEvaluatedMalformedKey,
                 })
        {
            var (cells, messages) = MapOne(Ambiguous(Cur(), outcome), Config());
            cells.Should().BeEmpty();
            messages.Should().BeEmpty();
        }
    }

    // ── 12. Coordinates: configured columns + explanation rows (no hardcoding) ─

    [Fact]
    public void CellCoordinates_UseConfiguredColumnsAndExplanationRows()
    {
        var cfg = Config(g: "X", j: "Y", p: "Z");
        var src = new Dictionary<int, (string?, object?, string?)> { [200] = ("WholeNumber", 5.0, "5") };
        var tgt = new Dictionary<int, TargetDvInfo> { [77] = TgtDv("WholeNumber") };

        var c = Cur(row: 77, expl: [Expl(null, 78), Expl(null, 79)]);
        var p = Prev(row: 200, providedBy: "PB", expl: [Expl("e1", 201), Expl("e2", 202)]);

        var (cells, messages) = MapOne(Agree(c, p), cfg, src, tgt);

        Cell(cells, 77, "X")!.TypedValue.Should().Be(5.0); // H→G at anchor, configured column
        Cell(cells, 78, "Y")!.Value.Should().Be("e1");     // K→J at explanation row 1
        Cell(cells, 79, "Y")!.Value.Should().Be("e2");     // K→J at explanation row 2
        Cell(cells, 77, "Z")!.Value.Should().Be("PB");     // O→P at anchor
        messages.Should().BeEmpty();
    }
}
