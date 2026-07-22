using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.GeneralDataValidationV01;

// Tests for GdAnswerDeviationCell.
// Assertions: Check + CellAddress (+ CheckResult substring), never id or total count (lesson 112).
public sealed class GdAnswerDeviationCellTests
{
    // ── Builders ────────────────────────────────────────────────────────────────

    // anchorRow identifies the answer; dvType drives the numeric gate; native (when supplied) is
    // the answer cell's typed value, resolved in preference to the text on its own side.
    private static GdAnswer Answer(string? value, int anchorRow,
                                   string? dvType = "WholeNumber", string? provided = null,
                                   object? native = null) =>
        new(AnswerId: "A-01", AnchorRow: anchorRow,
            PreviousAnswer: null, Answer: value, MaterialChange: null, ProvidedBy: provided,
            Explanations: [], AnswerDvType: dvType, AnswerNativeValue: native);

    private static GdV01Question Question(IReadOnlyList<GdAnswer> answers, int row = 10) =>
        new(RowNumber: row, XrefId: "Q1", OriginalText: "orig", QuestionText: "What?",
            SectionName: "G-ST", QuestionNumber: "1", Answers: answers);

    private static AlignedQuestion<GdV01Question> AqAgree(
        GdV01Question cur, GdV01Question prev) =>
        new(Current: cur, WithinYear: WithinYearJoin.JoinedByXrefId, TemplateMatch: null,
            RowShifted: false, TextMismatched: false,
            CrossYear: CrossYearOutcome.Agree, PreviousMatch: prev,
            XrefIdCounterpart: prev, MatcherCandidate: null, MatcherBaseScore: null);

    private static AlignedQuestion<GdV01Question> AqNeither(GdV01Question cur) =>
        new(Current: cur, WithinYear: WithinYearJoin.JoinedByXrefId, TemplateMatch: null,
            RowShifted: false, TextMismatched: false,
            CrossYear: CrossYearOutcome.Neither, PreviousMatch: null,
            XrefIdCounterpart: null, MatcherCandidate: null, MatcherBaseScore: null);

    private static AlignmentResult<GdV01Question> Result(params AlignedQuestion<GdV01Question>[] rows) =>
        new(rows.ToList(), Array.Empty<GdV01Question>(), Array.Empty<MalformedKey>());

    // The paired native selectors are wired in the SHARED builder, so every pre-existing assert
    // above also re-proves the pure text path is unchanged when neither side supplies a native
    // (the default). Each side reads its own answer's AnswerNativeValue.
    private static GdAnswerDeviationCell Check(double threshold = 0.25) =>
        new(currentValueSelector:   a => a.Answer,
            previousValueSelector:  a => a.Answer,
            dvTypeSelector:         a => a.AnswerDvType,
            providedBySelector:     a => a.ProvidedBy,
            column:                 "H",
            threshold:              threshold,
            currentNativeSelector:  a => a.AnswerNativeValue,
            previousNativeSelector: a => a.AnswerNativeValue);

    private static FindingEmitter Emitter(GdAnswerDeviationCell check) =>
        new(new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(check.Descriptors));

    // ── Core deviation logic ─────────────────────────────────────────────────────

    [Fact]
    public void Agree_NumericAnswers_RelChangeAboveThreshold_SingleFindingAtCurrentRow()
    {
        // 100 → 130: relative change = 0.30 ≥ 0.25 threshold → fire.
        var cur  = Question([Answer("130", 10, provided: "Unit-A")]);
        var prev = Question([Answer("100", 11)]);
        var check = Check(threshold: 0.25);

        var findings = check.Run(Result(AqAgree(cur, prev)), Emitter(check));

        var f = findings.Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.Deviation);
        f.CellAddresses.Should().Be("H10");
        f.CheckResult.Should().Contain("100");
        f.CheckResult.Should().Contain("130");
        f.ProvidedBy.Should().Be("Unit-A");
    }

    [Fact]
    public void Agree_NumericAnswers_RelChangeBelowThreshold_NoFinding()
    {
        // 100 → 110: relative change = 0.10 < 0.25 threshold → no finding.
        var cur  = Question([Answer("110", 10)]);
        var prev = Question([Answer("100", 11)]);
        var check = Check(threshold: 0.25);

        check.Run(Result(AqAgree(cur, prev)), Emitter(check)).Should().BeEmpty();
    }

    [Fact]
    public void Agree_ExactlyThreshold_Fires()
    {
        // 100 → 125: relative change = 0.25 exactly → inclusive comparison fires.
        var cur  = Question([Answer("125", 10)]);
        var prev = Question([Answer("100", 11)]);
        var check = Check(threshold: 0.25);

        check.Run(Result(AqAgree(cur, prev)), Emitter(check)).Should().ContainSingle();
    }

    [Fact]
    public void Agree_NonNumericDvType_NoFinding()
    {
        var cur  = Question([Answer("200", 10, dvType: "List")]);
        var prev = Question([Answer("100", 11, dvType: "List")]);
        var check = Check();

        check.Run(Result(AqAgree(cur, prev)), Emitter(check)).Should().BeEmpty();
    }

    [Fact]
    public void Agree_NullPreviousCounterpart_NoFinding()
    {
        // ToPrevious yields null counterpart when answer ID not found on previous side.
        // Use different AnswerIds by building questions manually.
        var curAnswer  = new GdAnswer("A-01", 10, null, "130", null, null, [], "WholeNumber");
        var prevAnswer = new GdAnswer("A-02", 11, null, "100", null, null, [], "WholeNumber");
        var cur  = new GdV01Question(10, "Q1", "orig", "What?", "G-ST", "1", [curAnswer]);
        var prev = new GdV01Question(11, "Q1", "orig", "What?", "G-ST", "1", [prevAnswer]);
        var check = Check();

        // ToPrevious pairing by AnswerId: A-01 → no counterpart A-01 on prev (prev has A-02 only).
        check.Run(Result(AqAgree(cur, prev)), Emitter(check)).Should().BeEmpty();
    }

    [Fact]
    public void Neither_CrossYear_NoFinding()
    {
        var cur  = Question([Answer("200", 10)]);
        var check = Check();

        check.Run(Result(AqNeither(cur)), Emitter(check)).Should().BeEmpty();
    }

    [Fact]
    public void Agree_PreviousValueZero_NoFinding()
    {
        var cur  = Question([Answer("100", 10)]);
        var prev = Question([Answer("0", 11)]);
        var check = Check();

        check.Run(Result(AqAgree(cur, prev)), Emitter(check)).Should().BeEmpty();
    }

    [Fact]
    public void Agree_BlankCurrentValue_NoFinding()
    {
        var cur  = Question([Answer(null, 10)]);
        var prev = Question([Answer("100", 11)]);
        var check = Check();

        check.Run(Result(AqAgree(cur, prev)), Emitter(check)).Should().BeEmpty();
    }

    [Fact]
    public void Agree_DecimalDvType_DeviationFires()
    {
        // Decimal is also a numeric type.
        var cur  = Question([Answer("2.0", 10, dvType: "Decimal")]);
        var prev = Question([Answer("1.0", 11, dvType: "Decimal")]);
        var check = Check(threshold: 0.25);

        check.Run(Result(AqAgree(cur, prev)), Emitter(check)).Should().ContainSingle();
    }

    // ── Native-value threading (BLG-0022/decimal-deviation — GD leg) ──────────────
    //
    // The text parse carries NumberStyles.AllowThousands, and under invariant culture the group
    // separator is ',' — so a European decimal comma is not REJECTED, it is silently CONSUMED
    // ("9,1" → 91). That corrupts the operands of |cur-prev|/|prev| rather than failing the parse,
    // which both fabricates findings and suppresses genuine ones. These asserts pin the PER-SIDE
    // native resolve (paired selectors) that fixes it. The text path itself is unchanged
    // (AllowThousands intact). Mirror of CrossYearDeviationCellTests §5 (Chunk 2).

    [Fact]
    public void MixedRendering_WithNatives_NoLongerFabricatesDeviation()
    {
        // The flip. Dot-rendered previous vs comma-rendered current, a 2.2% real change.
        // TEXT PATH:   9.1 vs "9,3"→93  →  921%  → a FABRICATED finding.
        // NATIVE PATH: 9.1 vs 9.3       →  2.2%  → nothing, which is correct.
        var cur  = Question([Answer("9,3", 10, dvType: "Decimal", native: 9.3d)]);
        var prev = Question([Answer("9.1", 11, dvType: "Decimal", native: 9.1d)]);
        var check = Check(threshold: 0.25);

        check.Run(Result(AqAgree(cur, prev)), Emitter(check)).Should().BeEmpty();
    }

    [Fact]
    public void CommaDecimals_GenuineDeviation_StillEmits_WithCorrectPercentage()
    {
        // CNV-0029. A real doubling must still surface, and the printed percentage (the ONLY place
        // a human sees the corruption) must be right. Bites on the percentage text, not presence.
        // NOTE: in this fixture both operands scale by the same factor 10 under the text path
        // (1000,0→10000, 2000,0→20000), so the ratio — and printed percentage — coincides between
        // the two paths; this pins the CNV-0029 property (a genuine deviation still surfaces with an
        // honest percentage), while the flip above carries the non-vacuity proof.
        var cur  = Question([Answer("2000,0", 10, dvType: "Decimal", native: 2000.0d)]);
        var prev = Question([Answer("1000,0", 11, dvType: "Decimal", native: 1000.0d)]);
        var check = Check(threshold: 0.25);

        var f = check.Run(Result(AqAgree(cur, prev)), Emitter(check)).Should().ContainSingle().Subject;
        f.CellAddresses.Should().Be("H10");
        f.CheckResult.Should().Contain("100 %");
        f.CheckResult.Should().Contain("1000,0").And.Contain("2000,0");
    }

    [Fact]
    public void PureNativePair_BehavesLikeTheEquivalentTextPair()
    {
        // Assert D — anti-skip. 100 → 150 is 50%; native-sourced operands must emit exactly as the
        // equivalent text pair does today. The fix never converts a real deviation into a skip.
        var cur  = Question([Answer("150", 10, native: 150.0d)]);
        var prev = Question([Answer("100", 11, native: 100.0d)]);
        var check = Check(threshold: 0.25);

        check.Run(Result(AqAgree(cur, prev)), Emitter(check))
            .Should().ContainSingle().Which.CellAddresses.Should().Be("H10");
    }

    [Fact]
    public void MixedSource_NativeOnOneSideTextOnTheOther_StillResolvesAndEmits()
    {
        // Assert E — the load-bearing one. Previous has NO native (text only); current has one.
        // Under an all-or-nothing fallback this pair would silently revert to text for BOTH sides;
        // per-side resolve takes each side from its best available source and emits.
        var cur  = Question([Answer("150", 10, native: 150.0d)]);
        var prev = Question([Answer("100", 11, native: null)]);
        var check = Check(threshold: 0.25);

        check.Run(Result(AqAgree(cur, prev)), Emitter(check))
            .Should().ContainSingle().Which.CellAddresses.Should().Be("H10");
    }

    [Fact]
    public void MixedSource_PerSideResolve_DoesNotRevertTheNativeSideToCorruptText()
    {
        // Assert E2 — the OTHER all-or-nothing variant ("require a native on BOTH sides before
        // trusting EITHER, else text for both"). E catches the skip-shaped variant; this catches the
        // revert-shaped one. prev has no native (text "100" → 100). cur is comma-rendered with a
        // native. Real change 0.5% < threshold → nothing.
        //   per-side  : 100 vs 100.5 → 0.5%  → empty (correct)
        //   revert-all: 100 vs "100,5"→1005 → 905% → a FABRICATED finding
        var cur  = Question([Answer("100,5", 10, dvType: "Decimal", native: 100.5d)]);
        var prev = Question([Answer("100", 11, dvType: "Decimal", native: null)]);
        var check = Check(threshold: 0.25);

        check.Run(Result(AqAgree(cur, prev)), Emitter(check)).Should().BeEmpty();
    }

    [Fact]
    public void NonDoubleNative_FallsBackToTextParse_Unchanged()
    {
        // Assert F — the `is double` guard rejects only what it must. A string / DateTime native is
        // not a comparable number, so both sides fall through to the text parse and behave
        // byte-identically to today (4 → 5 = 25% == threshold → emits).
        var cur  = Question([Answer("5", 10, native: new DateTime(2026, 7, 20))]);
        var prev = Question([Answer("4", 11, native: "4")]);
        var check = Check(threshold: 0.25);

        check.Run(Result(AqAgree(cur, prev)), Emitter(check))
            .Should().ContainSingle().Which.CellAddresses.Should().Be("H10");
    }

    [Fact]
    public void NonFiniteNative_FallsBackToTextParse_NeverPoisonsRelativeChange()
    {
        // Assert G — the mandatory double.IsFinite guard. NaN/∞ are doubles, so the `is double`
        // pattern alone would accept them and make relChange NaN — which compares false against every
        // threshold, silently SUPPRESSING the finding. IsFinite sends them to the text parse instead,
        // where 4 → 5 = 25% still emits.
        var cur  = Question([Answer("5", 10, native: double.PositiveInfinity)]);
        var prev = Question([Answer("4", 11, native: double.NaN)]);
        var check = Check(threshold: 0.25);

        check.Run(Result(AqAgree(cur, prev)), Emitter(check))
            .Should().ContainSingle().Which.CellAddresses.Should().Be("H10");
    }
}
