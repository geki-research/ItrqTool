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

    // anchorRow identifies the answer; dvType drives the numeric gate.
    private static GdAnswer Answer(string? value, int anchorRow,
                                   string? dvType = "WholeNumber", string? provided = null) =>
        new(AnswerId: "A-01", AnchorRow: anchorRow,
            PreviousAnswer: null, Answer: value, MaterialChange: null, ProvidedBy: provided,
            Explanations: [], AnswerDvType: dvType);

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

    private static GdAnswerDeviationCell Check(double threshold = 0.25) =>
        new(currentValueSelector:  a => a.Answer,
            previousValueSelector: a => a.Answer,
            dvTypeSelector:        a => a.AnswerDvType,
            providedBySelector:    a => a.ProvidedBy,
            column:                "H",
            threshold:             threshold);

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
}
