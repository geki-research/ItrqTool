using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Tasks.GeneralDataInject;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.GeneralDataValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.Shared;
using Xunit;

namespace ItrqTool.Tasks.Tests.GeneralDataInject;

/// <summary>
/// Pure unit tests for <see cref="GdInjectMapper"/> — no Excel, no mocks. Each test builds the
/// question/answer records, a <see cref="CrossFormatMatch{TCur,TPrev}"/> and the two H lookups
/// directly, then asserts the emitted (cells, messages) at PER-ANSWER grain — including the H→G
/// entry's <see cref="CellWriteEntry.TypedValue"/> on the typed arms.
/// </summary>
public sealed class GdInjectMapperTests
{
    // ── builders ──────────────────────────────────────────────────────────────

    // The mapper only reads these three columns from the config; the rest stay defaulted.
    private static GdV02Config Config(string g = "G", string j = "J", string p = "P") => new()
    {
        PreviousAnswerColumn = g,
        PreviousExplanationColumn = j,
        ProvidedByColumn = p,
    };

    private static GdExplanationRow Expl(string? current, int row)
        => new(Requested: null, Previous: null, Current: current, RowNumber: row);

    private static GdV02Answer CurAns(
        string? answerId = null,
        int anchorRow = 10,
        IReadOnlyList<GdExplanationRow>? expl = null) =>
        new(AnswerId: answerId,
            AnchorRow: anchorRow,
            PreviousAnswer: null,
            Answer: null,
            MaterialChange: null,
            HowExplanation: null,
            ProvidedBy: null,
            Explanations: expl ?? Array.Empty<GdExplanationRow>());

    private static GdAnswer PrevAns(
        string? answerId = null,
        int anchorRow = 100,
        string? answer = "3",
        string? providedBy = null,
        IReadOnlyList<GdExplanationRow>? expl = null) =>
        new(AnswerId: answerId,
            AnchorRow: anchorRow,
            PreviousAnswer: null,
            Answer: answer,
            MaterialChange: null,
            ProvidedBy: providedBy,
            Explanations: expl ?? Array.Empty<GdExplanationRow>());

    private static GdV02Question CurQ(
        string? xref = "GD-1",
        int rowNumber = 10,
        IReadOnlyList<GdV02Answer>? answers = null) =>
        new(RowNumber: rowNumber,
            XrefId: xref,
            OriginalText: "T",
            QuestionText: "T",
            SectionName: "S",
            QuestionNumber: "1",
            Answers: answers ?? new[] { CurAns() });

    private static GdV01Question PrevQ(
        string? xref = "GD-1",
        IReadOnlyList<GdAnswer>? answers = null) =>
        new(RowNumber: 100,
            XrefId: xref,
            OriginalText: "T",
            QuestionText: "T",
            SectionName: "S",
            QuestionNumber: "1",
            Answers: answers ?? new[] { PrevAns() });

    private static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) MapAgree(
        GdV02Question c,
        GdV01Question p,
        GdV02Config config,
        IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)>? sourceH = null,
        IReadOnlyDictionary<int, TargetDvInfo>? targetH = null)
    {
        var match = new CrossFormatMatch<GdV02Question, GdV01Question>(
            c, CrossYearOutcome.Agree, p, p, p, 0.95);
        var result = new CrossFormatAlignmentResult<GdV02Question, GdV01Question>(
            new[] { match }, Array.Empty<MalformedKey>());
        return GdInjectMapper.Map(
            result, config,
            sourceH ?? new Dictionary<int, (string?, object?, string?)>(),
            targetH ?? new Dictionary<int, TargetDvInfo>());
    }

    // Agree over a CHANGED text: same shape as MapAgree, but the caller supplies both texts and the
    // score the aligner would have computed, so the Warning's content can be asserted exactly.
    private static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) MapAgreeWithTexts(
        string currentText,
        string previousText,
        double? score,
        GdV02Config config,
        int currentRow = 10)
    {
        var c = CurQ(rowNumber: currentRow) with { OriginalText = currentText, QuestionText = currentText };
        var p = PrevQ() with { OriginalText = previousText, QuestionText = previousText };

        var match = new CrossFormatMatch<GdV02Question, GdV01Question>(
            c, CrossYearOutcome.Agree, p, p, p, score);
        var result = new CrossFormatAlignmentResult<GdV02Question, GdV01Question>(
            new[] { match }, Array.Empty<MalformedKey>());
        return GdInjectMapper.Map(
            result, config,
            new Dictionary<int, (string?, object?, string?)>(),
            new Dictionary<int, TargetDvInfo>());
    }

    private static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) MapDiverged(
        GdV02Question c, GdV01Question counterpart, double? score, GdV02Config config)
    {
        // Previous is null on this arm (no injection source); the counterpart is carried for
        // messaging only — exactly what the aligner emits below the threshold.
        var match = new CrossFormatMatch<GdV02Question, GdV01Question>(
            c, CrossYearOutcome.SameXrefIdTextDiverged, null, counterpart, counterpart, score);
        var result = new CrossFormatAlignmentResult<GdV02Question, GdV01Question>(
            new[] { match }, Array.Empty<MalformedKey>());
        return GdInjectMapper.Map(
            result, config,
            new Dictionary<int, (string?, object?, string?)>(),
            new Dictionary<int, TargetDvInfo>());
    }

    private static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) MapOutcome(
        GdV02Question c, CrossYearOutcome outcome, GdV02Config config)
    {
        var match = new CrossFormatMatch<GdV02Question, GdV01Question>(c, outcome, null, null, null, null);
        var result = new CrossFormatAlignmentResult<GdV02Question, GdV01Question>(
            new[] { match }, Array.Empty<MalformedKey>());
        return GdInjectMapper.Map(
            result, config,
            new Dictionary<int, (string?, object?, string?)>(),
            new Dictionary<int, TargetDvInfo>());
    }

    private static CellWriteEntry? Cell(IReadOnlyList<CellWriteEntry> cells, int row, string column)
        => cells.SingleOrDefault(e => e.Row == row && e.Column == column);

    // ── 1. H→G equal category → native, no message ────────────────────────────

    [Fact]
    public void Hg_EqualType_WritesNativeNoMessage()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("WholeNumber", 3.0, "3") };
        var tgt = new Dictionary<int, TargetDvInfo> { [10] = new TargetDvInfo("WholeNumber", null, null, null, null) };

        var (cells, messages) = MapAgree(CurQ(), PrevQ(), Config(), src, tgt);

        var g = Cell(cells, 10, "G");
        g.Should().NotBeNull();
        g!.TypedValue.Should().Be(3.0);
        g.TypedValue.Should().BeOfType<double>();
        messages.Should().BeEmpty();
    }

    // ── 2. H→G widen (WholeNumber → Decimal) → native + Info ──────────────────

    [Fact]
    public void Hg_WholeToDecimal_WritesNativePlusInfo()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("WholeNumber", 3.0, "3") };
        var tgt = new Dictionary<int, TargetDvInfo> { [10] = new TargetDvInfo("Decimal", null, null, null, null) };

        var (cells, messages) = MapAgree(CurQ(), PrevQ(), Config(), src, tgt);

        Cell(cells, 10, "G")!.TypedValue.Should().Be(3.0);
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Info);
        messages[0].Text.Should().Contain("widened");
    }

    // ── 3. H→G narrow (Decimal → WholeNumber), value does not conform → skip + Error ──

    [Fact]
    public void Hg_DecimalToWhole_SkipsWithError()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("Decimal", 3.5, "3.5") };
        var tgt = new Dictionary<int, TargetDvInfo> { [10] = new TargetDvInfo("WholeNumber", null, null, null, null) };

        var (cells, messages) = MapAgree(CurQ(), PrevQ(), Config(), src, tgt);

        Cell(cells, 10, "G").Should().BeNull(); // G skipped
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Error);
        messages[0].Text.Should().Contain("does not conform");
    }

    // ── 4. H→G incompatible → Error, skip G, continue (K→J / O→P still run) ────

    [Fact]
    public void Hg_IncompatibleTypes_EmitsErrorSkipsCellContinues()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("List", "Yes", "Yes") };
        var tgt = new Dictionary<int, TargetDvInfo> { [10] = new TargetDvInfo("WholeNumber", null, null, null, null) };

        var c = CurQ(answers: new[] { CurAns(anchorRow: 10, expl: new[] { Expl(null, 11) }) });
        var p = PrevQ(answers: new[]
        {
            PrevAns(anchorRow: 100, answer: "Yes", providedBy: "Bob", expl: new[] { Expl("expl-A", 101) })
        });

        var (cells, messages) = MapAgree(c, p, Config(), src, tgt);

        Cell(cells, 10, "G").Should().BeNull(); // G skipped
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Error);
        messages[0].Text.Should().Contain("does not conform");

        // The SAME answer's other writes still happen (continue, not abort).
        Cell(cells, 11, "J")!.Value.Should().Be("expl-A");
        Cell(cells, 10, "P")!.Value.Should().Be("Bob");
    }

    // ── 5. H→G blank source → omit cell, no message ───────────────────────────

    [Fact]
    public void Hg_BlankSource_OmitsCell()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("WholeNumber", null, null) };
        var tgt = new Dictionary<int, TargetDvInfo> { [10] = new TargetDvInfo("WholeNumber", null, null, null, null) };

        var (cells, messages) = MapAgree(CurQ(), PrevQ(), Config(), src, tgt);

        Cell(cells, 10, "G").Should().BeNull();
        messages.Should().BeEmpty();
    }

    // ── 5a. H→G List target, non-member source → skip + Error ─────────────────

    [Fact]
    public void Hg_ListTargetNonMemberSource_SkipsWithError()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("List", "Maybe", "Maybe") };
        var tgt = new Dictionary<int, TargetDvInfo> { [10] = new TargetDvInfo("List", null, null, null, ["Yes", "No"]) };

        var (cells, messages) = MapAgree(CurQ(), PrevQ(), Config(), src, tgt);

        Cell(cells, 10, "G").Should().BeNull();
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Error);
        messages[0].Text.Should().Contain("does not conform");
    }

    // ── 5b. H→G value-typed target, source violates the operator bound → skip + Error ──

    [Fact]
    public void Hg_ValueTypedTargetViolatesBound_SkipsWithError()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("WholeNumber", 20.0, "20") };
        var tgt = new Dictionary<int, TargetDvInfo> { [10] = new TargetDvInfo("WholeNumber", "LessThan", "10", null, null) };

        var (cells, messages) = MapAgree(CurQ(), PrevQ(), Config(), src, tgt);

        Cell(cells, 10, "G").Should().BeNull();
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Error);
        messages[0].Text.Should().Contain("does not conform");
    }

    // ── 5c. H→G comma-rendered decimal source, native in bound → injects (BL-058) ──

    [Fact]
    public void Hg_CommaDecimalSource_NativeInBound_Injects()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("Decimal", 9.1, "9,1") };
        var tgt = new Dictionary<int, TargetDvInfo> { [10] = new TargetDvInfo("Decimal", "Between", "0", "100", null) };

        var (cells, messages) = MapAgree(CurQ(), PrevQ(), Config(), src, tgt);

        var g = Cell(cells, 10, "G");
        g.Should().NotBeNull();
        g!.TypedValue.Should().Be(9.1);
        messages.Should().BeEmpty();
    }

    // ── 6. K→J position-aligned → one write per overlapping row ───────────────

    [Fact]
    public void KtoJ_PositionAligned_WritesPerRow()
    {
        var c = CurQ(answers: new[]
        {
            CurAns(anchorRow: 10, expl: new[] { Expl(null, 11), Expl(null, 12), Expl(null, 13) })
        });
        var p = PrevQ(answers: new[]
        {
            PrevAns(anchorRow: 100, expl: new[] { Expl("s1", 101), Expl("s2", 102), Expl("s3", 103) })
        });

        var (cells, messages) = MapAgree(c, p, Config());

        Cell(cells, 11, "J")!.Value.Should().Be("s1");
        Cell(cells, 12, "J")!.Value.Should().Be("s2");
        Cell(cells, 13, "J")!.Value.Should().Be("s3");
        messages.Should().BeEmpty();
    }

    // ── 7. K→J row-count mismatch → overlap written + ONE Warning ─────────────

    [Fact]
    public void KtoJ_RowCountMismatch_WritesOverlapPlusOneWarning()
    {
        var c = CurQ(answers: new[]
        {
            CurAns(anchorRow: 10, expl: new[] { Expl(null, 11), Expl(null, 12) })
        });
        var p = PrevQ(answers: new[]
        {
            PrevAns(anchorRow: 100, expl: new[] { Expl("s1", 101), Expl("s2", 102), Expl("s3", 103) })
        });

        var (cells, messages) = MapAgree(c, p, Config());

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
        var c = CurQ(answers: new[]
        {
            CurAns(anchorRow: 10, expl: new[] { Expl(null, 11), Expl(null, 12), Expl(null, 13) })
        });
        var p = PrevQ(answers: new[]
        {
            PrevAns(anchorRow: 100, expl: new[] { Expl("s1", 101), Expl("   ", 102), Expl("s3", 103) })
        });

        var (cells, messages) = MapAgree(c, p, Config());

        Cell(cells, 11, "J")!.Value.Should().Be("s1");
        Cell(cells, 12, "J").Should().BeNull();
        Cell(cells, 13, "J")!.Value.Should().Be("s3");
        messages.Should().BeEmpty();
    }

    // ── 9. O→P provided-by text; blank → omitted ──────────────────────────────

    [Fact]
    public void OtoP_ProvidedBy_WritesText()
    {
        var (cells, _) = MapAgree(
            CurQ(), PrevQ(answers: new[] { PrevAns(providedBy: "Carol") }), Config());
        var p = Cell(cells, 10, "P");
        p.Should().NotBeNull();
        p!.Value.Should().Be("Carol");
        p.TypedValue.Should().BeNull();

        var (blankCells, _) = MapAgree(
            CurQ(), PrevQ(answers: new[] { PrevAns(providedBy: "   ") }), Config());
        Cell(blankCells, 10, "P").Should().BeNull();
    }

    // ── 10. Ambiguous outcomes → one Warning, no cells ────────────────────────

    // WORDING-ONLY update by BLG-0077 (§1.2 "ordinary in-intent update", not an inversion): the
    // three outcomes used to share one arm and one message. The arm was split, so the two genuinely
    // ambiguous outcomes are asserted here — with their message text UNCHANGED, character for
    // character — and the text-diverged outcome moved to its own test below. The intent is
    // identical on both: exactly one Warning, zero cells.

    [Fact]
    public void Ambiguous_EmitsOneWarningNoCells()
    {
        var c = CurQ(xref: "GD-3", rowNumber: 42);

        foreach (var outcome in new[]
                 {
                     CrossYearOutcome.XrefIdConflict,
                     CrossYearOutcome.NewXrefIdWithLookalike,
                 })
        {
            var (cells, messages) = MapOutcome(c, outcome, Config());

            cells.Should().BeEmpty();
            messages.Should().ContainSingle();
            messages[0].Severity.Should().Be(MessageSeverity.Warning);
            messages[0].Text.Should().Contain("42");
            messages[0].Text.Should().Contain("GD-3");
            messages[0].Text.Should().Contain("ambiguous previous match");
            messages[0].Text.Should().Contain("left untouched");
        }
    }

    // Pins the exact legacy sentence for the two unchanged arms, so a future edit to the diverged
    // wording cannot drift into them unnoticed.
    [Theory]
    [InlineData(CrossYearOutcome.XrefIdConflict)]
    [InlineData(CrossYearOutcome.NewXrefIdWithLookalike)]
    public void AmbiguousArms_MessageText_IsUnchangedByTheSimilarityJoin(CrossYearOutcome outcome)
    {
        var c = CurQ(xref: "GD-3", rowNumber: 42);

        var (_, messages) = MapOutcome(c, outcome, Config());

        messages.Should().ContainSingle().Which.Text.Should().Be(
            $"Question GD-3 (row 42): ambiguous previous match ({outcome}) — left untouched.");
        messages[0].Text.Should().NotContain("similarity");
    }

    // ── 10b. Text below the threshold → its own Warning, still no cells ───────

    [Fact]
    public void TextDiverged_EmitsOneWarningNoCells_WithScoreAndBothTexts()
    {
        var c = CurQ(xref: "GD-3", rowNumber: 42);
        var p = PrevQ(xref: "GD-3");

        var (cells, messages) = MapDiverged(c, p, score: 0.3125, Config());

        cells.Should().BeEmpty("a below-threshold pair writes nothing, exactly as before");
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Warning);

        var text = messages[0].Text;
        text.Should().Contain("GD-3");
        text.Should().Contain("42");                     // current row
        text.Should().Contain("100");                    // previous row
        text.Should().Contain("0.3125");                 // the raw score, invariant-formatted
        text.Should().Contain("below the similarity threshold");
        text.Should().Contain("left untouched");
        text.Should().NotContain("ambiguous previous match");
    }

    // ── 10c. Agree over a CHANGED text → exactly one Warning, cells STILL written ─

    [Fact]
    public void Agree_IdenticalText_EmitsNoWarning()
    {
        var (_, messages) = MapAgreeWithTexts("Same text", "Same text", score: 1.0, Config());

        messages.Should().BeEmpty("an unchanged question is the ordinary case and says nothing");
    }

    [Fact]
    public void Agree_ChangedText_EmitsExactlyOneWarning_WithBothTextsBothRowsAndScore()
    {
        var (_, messages) = MapAgreeWithTexts(
            "Number of staff at year end 2025", "Number of staff at year end 2024",
            score: 0.9688, Config(), currentRow: 42);

        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Warning);

        var text = messages[0].Text;
        text.Should().Contain("GD-1");                                  // question identifier
        text.Should().Contain("42");                                    // current row
        text.Should().Contain("100");                                   // previous row
        text.Should().Contain("0.9688");                                // raw score
        text.Should().Contain("Number of staff at year end 2025");      // current text
        text.Should().Contain("Number of staff at year end 2024");      // previous text
        text.Should().Contain("question text changed");
        text.Should().Contain("carried forward");
    }

    [Fact]
    public void Agree_ChangedText_StillWritesTheCells()
    {
        // The Warning is an annunciation, not a veto: an above-threshold match still injects.
        var c = CurQ(rowNumber: 10) with { OriginalText = "New wording", QuestionText = "New wording" };
        var p = PrevQ(answers: new[] { PrevAns(anchorRow: 100, providedBy: "OU5") })
            with { OriginalText = "Old wording", QuestionText = "Old wording" };
        var match = new CrossFormatMatch<GdV02Question, GdV01Question>(
            c, CrossYearOutcome.Agree, p, p, p, 0.72);
        var result = new CrossFormatAlignmentResult<GdV02Question, GdV01Question>(
            new[] { match }, Array.Empty<MalformedKey>());

        var (cells, messages) = GdInjectMapper.Map(
            result, Config(),
            new Dictionary<int, (string?, object?, string?)>(),
            new Dictionary<int, TargetDvInfo>());

        messages.Should().ContainSingle().Which.Severity.Should().Be(MessageSeverity.Warning);
        Cell(cells, 10, "P").Should().NotBeNull("provided-by is still carried forward");
    }

    // Case-only drift scores 1.0 (TextSimilarity lowercases) but is a real change to the workbook,
    // so it must still be announced. This is why identity is ordinal and not "score == 1.0".
    [Fact]
    public void Agree_TextCaseOnlyDrift_StillWarns()
    {
        var (_, messages) = MapAgreeWithTexts("SAME TEXT", "same text", score: 1.0, Config());

        messages.Should().ContainSingle().Which.Severity.Should().Be(MessageSeverity.Warning);
    }

    // Exactly ONE Warning per question — not one per answer, not one per cell.
    [Fact]
    public void Agree_ChangedText_MultiAnswerQuestion_StillEmitsExactlyOneWarning()
    {
        var c = CurQ(rowNumber: 10, answers: new[]
        {
            CurAns(answerId: "a1", anchorRow: 10),
            CurAns(answerId: "a2", anchorRow: 11),
            CurAns(answerId: "a3", anchorRow: 12),
        }) with { OriginalText = "New wording", QuestionText = "New wording" };

        var p = PrevQ(answers: new[]
        {
            PrevAns(answerId: "a1", anchorRow: 100, providedBy: "OU"),
            PrevAns(answerId: "a2", anchorRow: 101, providedBy: "OU"),
            PrevAns(answerId: "a3", anchorRow: 102, providedBy: "OU"),
        }) with { OriginalText = "Old wording", QuestionText = "Old wording" };

        var match = new CrossFormatMatch<GdV02Question, GdV01Question>(
            c, CrossYearOutcome.Agree, p, p, p, 0.72);
        var result = new CrossFormatAlignmentResult<GdV02Question, GdV01Question>(
            new[] { match }, Array.Empty<MalformedKey>());

        var (_, messages) = GdInjectMapper.Map(
            result, Config(),
            new Dictionary<int, (string?, object?, string?)>(),
            new Dictionary<int, TargetDvInfo>());

        messages.Where(m => m.Text.Contains("question text changed"))
            .Should().HaveCount(1, "the Warning is per question, not per answer");
    }

    // Long questions are whitespace-collapsed and length-bounded so the Warning stays one readable
    // line; the untruncated text remains in the workbook.
    //
    // BLG-0079 IN-INTENT UPDATE (authorized by §1.2 / §3.3): the old intent was "bounded"; it is
    // now "bounded AROUND THE DRIFT". The old assertions are all retained verbatim — flattening,
    // ellipsis, overall length — and the drift-visibility assertion is added. The old fixture
    // happened to differ at character 0, so it passed either way and proved nothing about
    // windowing; the drift now sits at the END, which is precisely the case head-truncation lost.
    [Fact]
    public void Agree_ChangedText_LongTextIsFlattenedAndBoundedAroundTheDrift()
    {
        var common       = new string('x', 400);
        var longCurrent  = common + "\n\tas of 2025";
        var longPrevious = common + " as of 2024";

        var (_, messages) = MapAgreeWithTexts(longCurrent, longPrevious, score: 0.98, Config());

        var text = messages.Should().ContainSingle().Subject.Text;
        text.Should().NotContain("\n").And.NotContain("\t");
        text.Should().Contain("…", "the excerpt is elided rather than dumped in full");
        text.Length.Should().BeLessThan(600, "two bounded excerpts plus a fixed preamble");

        // The point of the window: both years' differing tails survive into the message.
        text.Should().Contain("as of 2025", "the current drift must be visible");
        text.Should().Contain("as of 2024", "the previous drift must be visible");
    }

    // A null score must not throw or read as "completely dissimilar".
    [Fact]
    public void Agree_ChangedText_NullScore_RendersUnknownNotZero()
    {
        var (_, messages) = MapAgreeWithTexts("New wording", "Old wording", score: null, Config());

        var text = messages.Should().ContainSingle().Subject.Text;
        text.Should().Contain("unknown");
        text.Should().NotContain("0.0000");
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
            var (cells, messages) = MapOutcome(CurQ(), outcome, Config());
            cells.Should().BeEmpty();
            messages.Should().BeEmpty();
        }
    }

    // ── 12. Multi-answer question → paired by AnswerId, each writes at its anchor ─

    [Fact]
    public void MultiAnswer_PairedByAnswerId_EachAnswerWritesAtItsAnchor()
    {
        var src = new Dictionary<int, (string?, object?, string?)>
        {
            [100] = ("List", "x", "x"),
            [200] = ("List", "y", "y"),
        };
        var tgt = new Dictionary<int, TargetDvInfo>
        {
            [10] = new TargetDvInfo("List", null, null, null, ["x", "y"]),
            [20] = new TargetDvInfo("List", null, null, null, ["x", "y"]),
        };

        var c = CurQ(answers: new[]
        {
            CurAns(answerId: "A-01", anchorRow: 10),
            CurAns(answerId: "A-02", anchorRow: 20),
        });
        var p = PrevQ(answers: new[]
        {
            PrevAns(answerId: "A-01", anchorRow: 100, answer: "x", providedBy: "Bob"),
            PrevAns(answerId: "A-02", anchorRow: 200, answer: "y", providedBy: "Sue"),
        });

        var (cells, messages) = MapAgree(c, p, Config(), src, tgt);

        Cell(cells, 10, "G")!.Value.Should().Be("x");
        Cell(cells, 20, "G")!.Value.Should().Be("y");
        Cell(cells, 10, "P")!.Value.Should().Be("Bob");
        Cell(cells, 20, "P")!.Value.Should().Be("Sue");
        messages.Should().BeEmpty();
    }

    // ── 13. Current answer with no v01 counterpart → no writes for that answer ──

    [Fact]
    public void UnmatchedCurrentAnswer_NoWrites()
    {
        var src = new Dictionary<int, (string?, object?, string?)> { [100] = ("List", "x", "x") };
        var tgt = new Dictionary<int, TargetDvInfo>
        {
            [10] = new TargetDvInfo("List", null, null, null, ["x", "y"]),
            [20] = new TargetDvInfo("List", null, null, null, ["x", "y"]),
        };

        var c = CurQ(answers: new[]
        {
            CurAns(answerId: "A-01", anchorRow: 10),
            CurAns(answerId: "A-99", anchorRow: 20),   // no counterpart in previous
        });
        var p = PrevQ(answers: new[]
        {
            PrevAns(answerId: "A-01", anchorRow: 100, answer: "x", providedBy: "Bob"),
        });

        var (cells, messages) = MapAgree(c, p, Config(), src, tgt);

        Cell(cells, 10, "G")!.Value.Should().Be("x");
        Cell(cells, 10, "P")!.Value.Should().Be("Bob");
        Cell(cells, 20, "G").Should().BeNull();
        Cell(cells, 20, "P").Should().BeNull();
        messages.Should().BeEmpty();
    }

    // ── 14. Coordinates honour configured columns + explanation rows ──────────

    [Fact]
    public void CellCoordinates_UseConfiguredColumnsAndExplanationRows()
    {
        var cfg = Config(g: "X", j: "Y", p: "Z");
        var src = new Dictionary<int, (string?, object?, string?)> { [200] = ("WholeNumber", 5.0, "5") };
        var tgt = new Dictionary<int, TargetDvInfo> { [77] = new TargetDvInfo("WholeNumber", null, null, null, null) };

        var c = CurQ(answers: new[]
        {
            CurAns(anchorRow: 77, expl: new[] { Expl(null, 78), Expl(null, 79) })
        });
        var p = PrevQ(answers: new[]
        {
            PrevAns(anchorRow: 200, providedBy: "PB", expl: new[] { Expl("e1", 201), Expl("e2", 202) })
        });

        var (cells, messages) = MapAgree(c, p, cfg, src, tgt);

        Cell(cells, 77, "X")!.TypedValue.Should().Be(5.0); // H→G at anchor, configured column
        Cell(cells, 78, "Y")!.Value.Should().Be("e1");     // K→J at explanation row 1
        Cell(cells, 79, "Y")!.Value.Should().Be("e2");     // K→J at explanation row 2
        Cell(cells, 77, "Z")!.Value.Should().Be("PB");     // O→P at anchor
        messages.Should().BeEmpty();
    }
}
