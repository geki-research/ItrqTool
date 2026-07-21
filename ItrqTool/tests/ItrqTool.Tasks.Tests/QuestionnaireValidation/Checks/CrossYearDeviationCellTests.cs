using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Checks;

// Coverage for the CrossYearDeviationCell<T> wrapper (finding 6a): the cross-year Agree gate
// (PreviousMatch non-null IFF Agree), the numeric-only DV-type filter (WholeNumber/Decimal only;
// List/Text/AnyValue/Date skipped), the present-gate + invariant parse-gate, the zero-base skip
// (prev == 0), the RELATIVE threshold compare (|cur - prev| / |prev| >= threshold, threshold a
// FRACTION, inclusive), and the anchor address + Deviation check + Warning default. Mirrors
// DvConformanceCellTests' direct AlignedQuestion-builder style; constructs real RlqV01Question
// records so the generic check is exercised through its production type parameter.
public sealed class CrossYearDeviationCellTests
{
    private const string Role = "answer";
    private const string Column = "H";
    private const double Threshold = 0.25; // 25% relative year-over-year change.

    // nativeSelector is wired in the SHARED builder, so every pre-existing assert below also
    // re-proves the pure text path is unchanged when no native is supplied (Q's default).
    private static CrossYearDeviationCell<RlqV01Question> Primitive(double threshold = Threshold) =>
        new(answerSelector:         q => q.Answer,
            templateDvTypeSelector: q => q.AnswerDvType,
            currentDvTypeSelector:  q => q.AnswerDvType,
            providedBySelector:     q => q.ProvidedBy,
            role:      Role,
            column:    Column,
            threshold: threshold,
            nativeSelector: q => q.AnswerNativeValue);

    // Real RlqV01Question with just the fields 6a reads; everything else null/empty.
    private static RlqV01Question Q(
        int row, string? answer, string? dvType = "WholeNumber", string? providedBy = null,
        object? native = null) =>
        new(
            RowNumber: row,
            XrefId: "x1",
            OriginalText: "Question text",
            QuestionText: "Question text",
            SectionName: "Section",
            QuestionNumber: row.ToString(),
            Guidance: null,
            RequestedType: null,
            PreviousAnswer: null,
            Answer: answer,
            AnswerDvType: dvType,
            AnswerDvFormula: null,
            AnswerDvOperator: null,
            AnswerDvFormula2: null,
            MaterialChange: null,
            MaterialChangeDvType: null,
            MaterialChangeDvFormula: null,
            MaterialChangeDvOperator: null,
            MaterialChangeDvFormula2: null,
            ProvidedBy: providedBy,
            ExplanationRows: Array.Empty<RlqExplanationRow>(),
            AnswerNativeValue: native);

    private static AlignedQuestion<RlqV01Question> Aq(
        RlqV01Question cur,
        RlqV01Question? prev,
        CrossYearOutcome crossYear = CrossYearOutcome.Agree,
        RlqV01Question? tmpl = null) =>
        new(Current: cur, WithinYear: WithinYearJoin.JoinedByXrefId, TemplateMatch: tmpl ?? prev,
            RowShifted: false, TextMismatched: false,
            CrossYear: crossYear, PreviousMatch: prev,
            XrefIdCounterpart: null, MatcherCandidate: null, MatcherBaseScore: null);

    private static AlignmentResult<RlqV01Question> Result(params AlignedQuestion<RlqV01Question>[] rows) =>
        new(rows.ToList(), Array.Empty<RlqV01Question>(), Array.Empty<MalformedKey>());

    private static FindingEmitter Emitter(
        CrossYearDeviationCell<RlqV01Question> primitive,
        IReadOnlyDictionary<string, FindingEvaluation>? overrides = null) =>
        new(overrides ?? new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(primitive.Descriptors));

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Descriptor_IsCrossYearAnswerDeviation_WarningDeviation()
    {
        var d = Primitive().Descriptors.Should().ContainSingle().Subject;
        d.Id.Should().Be("cross-year.answer-deviation");
        d.Check.Should().Be(ValidationCheck.Deviation);
        d.DefaultEvaluation.Should().Be(FindingEvaluation.Warning);
        d.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void WholeNumber_RelativeChangeExactlyThreshold_EmitsAtAnchor_Warning()
    {
        // prev 4 -> cur 5: |5 - 4| / |4| = 0.25 == threshold. The compare is >= (inclusive) → emits.
        var cur = Q(6, "5", providedBy: "Unit-B");
        var prev = Q(6, "4");
        var p = Primitive();

        var f = p.Run(Result(Aq(cur, prev)), Emitter(p)).Should().ContainSingle().Subject;

        f.Check.Should().Be(ValidationCheck.Deviation);
        f.Evaluation.Should().Be(FindingEvaluation.Warning);
        f.CellAddresses.Should().Be("H6");
        f.ProvidedBy.Should().Be("Unit-B");
        f.CheckResult.Should().Contain("H6");
    }

    [Fact]
    public void WholeNumber_RelativeChangeBelowThreshold_NoFinding()
    {
        // prev 5 -> cur 6: |6 - 5| / |5| = 0.20 < 0.25 → no finding.
        var p = Primitive();
        p.Run(Result(Aq(Q(7, "6"), Q(7, "5"))), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void WholeNumber_RelativeChangeAboveThreshold_Emits()
    {
        // prev 4 -> cur 6: |6 - 4| / |4| = 0.50 >= 0.25 → one finding at the anchor.
        var p = Primitive();
        p.Run(Result(Aq(Q(6, "6"), Q(6, "4"))), Emitter(p))
            .Should().ContainSingle().Which.CellAddresses.Should().Be("H6");
    }

    [Fact]
    public void Decimal_RelativeChangeBelowThreshold_NoFinding()
    {
        // prev 4.0 -> cur 4.5: |4.5 - 4.0| / |4.0| = 0.125 < 0.25, DV type Decimal → no finding.
        var cur = Q(6, "4.5", dvType: "Decimal");
        var prev = Q(6, "4.0", dvType: "Decimal");
        var p = Primitive();
        p.Run(Result(Aq(cur, prev)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void PreviousZero_NoFinding_ZeroBaseSkip()
    {
        // prev == 0 has no percentage base → skipped, regardless of the current value.
        var p = Primitive();
        p.Run(Result(Aq(Q(6, "3"), Q(6, "0"))), Emitter(p)).Should().BeEmpty();
        p.Run(Result(Aq(Q(6, "0"), Q(6, "0"))), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void NegativePrevious_RelativeChangeUsesAbsoluteDenominator_Emits()
    {
        // prev -4 -> cur -5: |-5 - (-4)| / |-4| = 1/4 = 0.25 == threshold → one finding.
        var p = Primitive();
        p.Run(Result(Aq(Q(6, "-5"), Q(6, "-4"))), Emitter(p))
            .Should().ContainSingle().Which.CellAddresses.Should().Be("H6");
    }

    [Theory]
    [InlineData("List")]
    [InlineData("Text")]
    [InlineData("AnyValue")]
    [InlineData("Date")]
    [InlineData(null)]
    public void NonNumericDvType_NoFinding_EvenWhenValuesNumericLooking(string? dvType)
    {
        // Values are numeric and far apart (would be 400%), but the DV type is not
        // WholeNumber/Decimal → skipped.
        var cur = Q(6, "5", dvType: dvType);
        var prev = Q(6, "1", dvType: dvType);
        var p = Primitive();
        p.Run(Result(Aq(cur, prev)), Emitter(p)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(CrossYearOutcome.Neither)]
    [InlineData(CrossYearOutcome.XrefIdConflict)]
    [InlineData(CrossYearOutcome.NewXrefIdWithLookalike)]
    [InlineData(CrossYearOutcome.SameXrefIdTextDiverged)]
    public void NotAgree_NoFinding(CrossYearOutcome outcome)
    {
        // Big relative delta, but not a confident (Agree) match → no baseline, no deviation.
        var cur = Q(6, "5");
        var prev = Q(6, "1");
        var p = Primitive();
        p.Run(Result(Aq(cur, prev, crossYear: outcome)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void BlankCurrentOrPreviousAnswer_NoFinding_PresentGate()
    {
        var p = Primitive();
        p.Run(Result(Aq(Q(6, "   "), Q(6, "4"))), Emitter(p)).Should().BeEmpty();
        p.Run(Result(Aq(Q(6, "5"), Q(6, null))), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void UnparseableEitherSide_NoFinding_ParseGate()
    {
        var p = Primitive();
        p.Run(Result(Aq(Q(6, "abc"), Q(6, "4"))), Emitter(p)).Should().BeEmpty();
        p.Run(Result(Aq(Q(6, "5"), Q(6, "n/a"))), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void SeverityOverride_Applies()
    {
        // prev 1 -> cur 5: |5 - 1| / |1| = 4.0 >= 0.25 → emits; override raises it to Error.
        var p = Primitive();
        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            ["cross-year.answer-deviation"] = FindingEvaluation.Error,
        };
        p.Run(Result(Aq(Q(6, "5"), Q(6, "1"))), Emitter(p, overrides))
            .Should().ContainSingle().Which.Evaluation.Should().Be(FindingEvaluation.Error);
    }

    // ── Native-value threading (BLG-0022/decimal-deviation) ──────────────────────
    //
    // The text parse carries NumberStyles.AllowThousands, and under invariant culture the group
    // separator is ',' — so a European decimal comma is not REJECTED, it is silently CONSUMED
    // ("9,1" → 91). That corrupts the operands of |cur-prev|/|prev| rather than failing the parse,
    // which both fabricates findings and suppresses genuine ones. These asserts pin the per-side
    // native resolve that fixes it. The text path itself is unchanged (AllowThousands intact).

    [Fact]
    public void MixedRendering_WithNatives_NoLongerFabricatesDeviation()
    {
        // §5.1 — the flip. Dot-rendered previous vs comma-rendered current, a 2.2% real change.
        // TEXT PATH:   9.1 vs "9,3"→93  →  921%  → a FABRICATED finding.
        // NATIVE PATH: 9.1 vs 9.3       →  2.2%  → nothing, which is correct.
        var prev = Q(6, "9.1", dvType: "Decimal", native: 9.1d);
        var cur = Q(6, "9,3", dvType: "Decimal", native: 9.3d);
        var p = Primitive();

        p.Run(Result(Aq(cur, prev)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void CommaDecimals_GenuineDeviation_StillEmits_WithCorrectPercentage()
    {
        // §5.2 — CNV-0029. A real doubling must still surface, and the printed percentage (the ONLY
        // place a human sees the corruption) must now be right.
        // TEXT PATH:   10000 vs 20000 → 100% → right verdict, corrupt operands.
        // NATIVE PATH:  1000 vs  2000 → 100% → same verdict, honest operands.
        var prev = Q(6, "1000,0", dvType: "Decimal", native: 1000.0d);
        var cur = Q(6, "2000,0", dvType: "Decimal", native: 2000.0d);
        var p = Primitive();

        var f = p.Run(Result(Aq(cur, prev)), Emitter(p)).Should().ContainSingle().Subject;
        f.CellAddresses.Should().Be("H6");
        // Bites on the printed percentage, not mere presence (invariant P0 renders "100 %").
        // NOTE: in THIS fixture both operands are scaled by the same factor 10 under the text
        // path (1000,0→10000, 2000,0→20000), so the ratio — and thus the printed percentage —
        // coincides between the two paths. This assert therefore pins the CNV-0029 property
        // (a genuine deviation still surfaces, with an honest percentage) but is NOT the
        // non-vacuity proof; §5.1's flip carries that.
        f.CheckResult.Should().Contain("100 %");
        f.CheckResult.Should().Contain("1000,0").And.Contain("2000,0");
    }

    [Fact]
    public void PureNativePair_BehavesLikeTheEquivalentTextPair()
    {
        // Assert D — anti-skip. 100 → 150 is a 50% change; native-sourced operands must emit
        // exactly as the equivalent text pair does today. The fix never converts a real
        // deviation into a skip.
        var prev = Q(6, "100", native: 100.0d);
        var cur = Q(6, "150", native: 150.0d);
        var p = Primitive();

        p.Run(Result(Aq(cur, prev)), Emitter(p))
            .Should().ContainSingle().Which.CellAddresses.Should().Be("H6");
    }

    [Fact]
    public void MixedSource_NativeOnOneSideTextOnTheOther_StillResolvesAndEmits()
    {
        // Assert E — the load-bearing one. Previous has NO native (text only); current has one.
        // Under an all-or-nothing fallback this pair would silently revert to the text path for
        // BOTH sides; per-side resolve takes each side from its best available source and emits.
        var prev = Q(6, "100", native: null);
        var cur = Q(6, "150", native: 150.0d);
        var p = Primitive();

        p.Run(Result(Aq(cur, prev)), Emitter(p))
            .Should().ContainSingle().Which.CellAddresses.Should().Be("H6");
    }

    [Fact]
    public void MixedSource_PerSideResolve_DoesNotRevertTheNativeSideToCorruptText()
    {
        // Assert E2 — the OTHER all-or-nothing variant, the one named in the design rationale:
        // "require a native on BOTH sides before trusting EITHER, else use text for both".
        // Assert E above catches the skip-shaped variant; this catches the revert-shaped one.
        //
        // prev has no native (text "100" parses honestly → 100). cur is comma-rendered with a
        // native. Real change is 0.5% — well under threshold, so nothing should be emitted.
        //   per-side  : 100 vs 100.5 → 0.5%  → empty (correct)
        //   revert-all: 100 vs "100,5"→1005 → 905% → a FABRICATED finding
        var prev = Q(6, "100", dvType: "Decimal", native: null);
        var cur = Q(6, "100,5", dvType: "Decimal", native: 100.5d);
        var p = Primitive();

        p.Run(Result(Aq(cur, prev)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void NonDoubleNative_FallsBackToTextParse_Unchanged()
    {
        // Assert F — the `is double` guard rejects only what it must. A string / DateTime native
        // is not a comparable number, so both sides fall through to the text parse and behave
        // byte-identically to today (4 → 5 = 25% == threshold → emits).
        var prev = Q(6, "4", native: "4");
        var cur = Q(6, "5", native: new DateTime(2026, 7, 20));
        var p = Primitive();

        p.Run(Result(Aq(cur, prev)), Emitter(p))
            .Should().ContainSingle().Which.CellAddresses.Should().Be("H6");
    }

    [Fact]
    public void NonFiniteNative_FallsBackToTextParse_NeverPoisonsRelativeChange()
    {
        // Assert G — hazard 2 (the mandatory double.IsFinite guard). NaN/∞ are doubles, so the
        // `is double` pattern alone would accept them and make relChange NaN — which compares
        // false against every threshold, silently SUPPRESSING the finding. IsFinite sends them to
        // the text parse instead, where 4 → 5 = 25% still emits.
        var prev = Q(6, "4", native: double.NaN);
        var cur = Q(6, "5", native: double.PositiveInfinity);
        var p = Primitive();

        p.Run(Result(Aq(cur, prev)), Emitter(p))
            .Should().ContainSingle().Which.CellAddresses.Should().Be("H6");
    }
}
