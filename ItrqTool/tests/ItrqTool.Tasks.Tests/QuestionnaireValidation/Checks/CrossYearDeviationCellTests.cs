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
// List/Text/AnyValue/Date skipped), the present-gate + invariant parse-gate, the threshold compare
// (|cur - prev| >= threshold), and the anchor address + Deviation check + Warning default. Mirrors
// DvConformanceCellTests' direct AlignedQuestion-builder style; constructs real RlqV01Question
// records so the generic check is exercised through its production type parameter.
public sealed class CrossYearDeviationCellTests
{
    private const string Role = "answer";
    private const string Column = "H";
    private const double Threshold = 2;

    private static CrossYearDeviationCell<RlqV01Question> Primitive(double threshold = Threshold) =>
        new(answerSelector:         q => q.Answer,
            templateDvTypeSelector: q => q.AnswerDvType,
            currentDvTypeSelector:  q => q.AnswerDvType,
            providedBySelector:     q => q.ProvidedBy,
            role:      Role,
            column:    Column,
            threshold: threshold);

    // Real RlqV01Question with just the fields 6a reads; everything else null/empty.
    private static RlqV01Question Q(
        int row, string? answer, string? dvType = "WholeNumber", string? providedBy = null) =>
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
            ExplanationRows: Array.Empty<RlqExplanationRow>());

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
    public void WholeNumber_DeltaAtOrAboveThreshold_EmitsAtAnchor_Warning()
    {
        var cur = Q(6, "5", providedBy: "Unit-B");
        var prev = Q(6, "1");
        var p = Primitive();

        var f = p.Run(Result(Aq(cur, prev)), Emitter(p)).Should().ContainSingle().Subject;

        f.Check.Should().Be(ValidationCheck.Deviation);
        f.Evaluation.Should().Be(FindingEvaluation.Warning);
        f.CellAddresses.Should().Be("H6");
        f.ProvidedBy.Should().Be("Unit-B");
        f.CheckResult.Should().Contain("H6");
    }

    [Fact]
    public void WholeNumber_DeltaExactlyThreshold_Emits()
    {
        // delta = |3 - 1| = 2 == threshold → the comparison is >=, so this emits.
        var p = Primitive();
        p.Run(Result(Aq(Q(7, "3"), Q(7, "1"))), Emitter(p))
            .Should().ContainSingle().Which.CellAddresses.Should().Be("H7");
    }

    [Fact]
    public void WholeNumber_DeltaBelowThreshold_NoFinding()
    {
        // delta = |2 - 1| = 1 < 2 → no finding.
        var p = Primitive();
        p.Run(Result(Aq(Q(6, "2"), Q(6, "1"))), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void Decimal_FractionalDeltaAtOrAboveThreshold_Emits()
    {
        // delta = |3.5 - 1.0| = 2.5 >= 2, DV type Decimal → emits.
        var cur = Q(6, "3.5", dvType: "Decimal");
        var prev = Q(6, "1.0", dvType: "Decimal");
        var p = Primitive();
        p.Run(Result(Aq(cur, prev)), Emitter(p))
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
        // Values are numeric and far apart, but the DV type is not WholeNumber/Decimal → skipped.
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
        // Big numeric delta, but not a confident (Agree) match → no baseline, no deviation.
        var cur = Q(6, "5");
        var prev = Q(6, "1");
        var p = Primitive();
        p.Run(Result(Aq(cur, prev, crossYear: outcome)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void BlankCurrentOrPreviousAnswer_NoFinding_PresentGate()
    {
        var p = Primitive();
        p.Run(Result(Aq(Q(6, "   "), Q(6, "1"))), Emitter(p)).Should().BeEmpty();
        p.Run(Result(Aq(Q(6, "5"), Q(6, null))), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void UnparseableEitherSide_NoFinding_ParseGate()
    {
        var p = Primitive();
        p.Run(Result(Aq(Q(6, "abc"), Q(6, "1"))), Emitter(p)).Should().BeEmpty();
        p.Run(Result(Aq(Q(6, "5"), Q(6, "n/a"))), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void SeverityOverride_Applies()
    {
        var p = Primitive();
        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            ["cross-year.answer-deviation"] = FindingEvaluation.Error,
        };
        p.Run(Result(Aq(Q(6, "5"), Q(6, "1"))), Emitter(p, overrides))
            .Should().ContainSingle().Which.Evaluation.Should().Be(FindingEvaluation.Error);
    }
}
