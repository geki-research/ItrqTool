using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.GeneralDataValidationV01;

// C1 lock-in: the GD per-ANSWER surface the five C2 checks build on —
//   - GdAnswerJoin (cur↔tmpl, cur↔prev by AnswerId; bare-qid → ""; order preserved; null sides),
//   - GdPerAnswerEmit (the per-answer cell address + the BL-037 section-gate mechanism),
//   - GdAnswerFrozenConstraintCell (the reference check: join + per-answer emit + kernel reuse).
// In-memory GdV01Question/GdAnswer only (no file fixtures). Assertions are by Check + cell
// address (+ CheckResult substring), never by id or total count (lesson 112).
public sealed class GdPerAnswerSurfaceTests
{
    // ── Builders ────────────────────────────────────────────────────────────────

    private static GdAnswer Answer(
        string? answerId, int anchorRow,
        string? dvType = null, string? dvOp = null, string? dvFormula = null, string? dvFormula2 = null,
        string? providedBy = null) =>
        new(AnswerId: answerId, AnchorRow: anchorRow,
            PreviousAnswer: null, Answer: null, MaterialChange: null, ProvidedBy: providedBy,
            Explanations: [],
            AnswerDvType: dvType, AnswerDvFormula: dvFormula, AnswerDvOperator: dvOp, AnswerDvFormula2: dvFormula2);

    private static GdV01Question Question(
        IReadOnlyList<GdAnswer> answers, int rowNumber = 10, string section = "G-ST") =>
        new(RowNumber: rowNumber, XrefId: "Q1", OriginalText: "orig", QuestionText: "What?",
            SectionName: section, QuestionNumber: "1", Answers: answers);

    private static AlignedQuestion<GdV01Question> Aq(
        GdV01Question cur,
        WithinYearJoin withinYear = WithinYearJoin.JoinedByXrefId,
        GdV01Question? tmpl = null,
        CrossYearOutcome crossYear = CrossYearOutcome.Neither,
        GdV01Question? prev = null) =>
        new(Current: cur,
            WithinYear: withinYear,
            TemplateMatch: tmpl,
            RowShifted: false,
            TextMismatched: false,
            CrossYear: crossYear,
            PreviousMatch: prev,
            XrefIdCounterpart: null,
            MatcherCandidate: null,
            MatcherBaseScore: null);

    private static AlignmentResult<GdV01Question> Result(params AlignedQuestion<GdV01Question>[] rows) =>
        new(rows.ToList(), Array.Empty<GdV01Question>(), Array.Empty<MalformedKey>());

    private static GdAnswerFrozenConstraintCell FrozenCheck() =>
        new(a => a.AnswerDvType, a => a.AnswerDvOperator, a => a.AnswerDvFormula, a => a.AnswerDvFormula2,
            role: "answer-dv", column: "H");

    private static FindingEmitter Emitter(GdAnswerFrozenConstraintCell check) =>
        new(new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(check.Descriptors));

    // ── GdAnswerJoin ────────────────────────────────────────────────────────────

    [Fact]
    public void Join_ToTemplate_PairsByAnswerId_OrderPreserved()
    {
        // current order A-03, A-01, A-02; template shuffled — pairing is by id, order = current's.
        var cur = Question([Answer("A-03", 30), Answer("A-01", 10), Answer("A-02", 20)]);
        var tmpl = Question([Answer("A-01", 11), Answer("A-02", 21), Answer("A-03", 31)]);

        var pairs = GdAnswerJoin.ToTemplate(Aq(cur, tmpl: tmpl));

        pairs.Select(p => p.Current.AnswerId).Should().Equal("A-03", "A-01", "A-02");
        pairs.Select(p => p.Counterpart!.AnswerId).Should().Equal("A-03", "A-01", "A-02");
        pairs.Select(p => p.Counterpart!.AnchorRow).Should().Equal(31, 11, 21);
    }

    [Fact]
    public void Join_BareQid_PairsToBareQid()
    {
        var cur = Question([Answer(null, 10)]);
        var tmpl = Question([Answer(null, 11)]);

        var pairs = GdAnswerJoin.ToTemplate(Aq(cur, tmpl: tmpl));

        pairs.Should().ContainSingle();
        pairs[0].Counterpart.Should().NotBeNull();
        pairs[0].Counterpart!.AnchorRow.Should().Be(11);
    }

    [Fact]
    public void Join_CurrentAnswerAbsentOnTemplate_NullCounterpart()
    {
        var cur = Question([Answer("A-01", 10), Answer("A-99", 90)]);
        var tmpl = Question([Answer("A-01", 11)]);

        var pairs = GdAnswerJoin.ToTemplate(Aq(cur, tmpl: tmpl));

        pairs.Should().HaveCount(2);
        pairs.Single(p => p.Current.AnswerId == "A-01").Counterpart.Should().NotBeNull();
        pairs.Single(p => p.Current.AnswerId == "A-99").Counterpart.Should().BeNull();
    }

    [Fact]
    public void Join_TemplateAnswerAbsentOnCurrent_NotEmittedAsCurrentRow()
    {
        var cur = Question([Answer("A-01", 10)]);
        var tmpl = Question([Answer("A-01", 11), Answer("A-02", 21)]);

        var pairs = GdAnswerJoin.ToTemplate(Aq(cur, tmpl: tmpl));

        // one pair per CURRENT answer — the extra template A-02 produces no row.
        pairs.Should().ContainSingle().Which.Current.AnswerId.Should().Be("A-01");
    }

    [Fact]
    public void Join_NullTemplateMatch_AllNullCounterparts()
    {
        var cur = Question([Answer("A-01", 10), Answer("A-02", 20)]);

        var pairs = GdAnswerJoin.ToTemplate(Aq(cur, withinYear: WithinYearJoin.AddedInResponse, tmpl: null));

        pairs.Should().HaveCount(2);
        pairs.Should().OnlyContain(p => p.Counterpart == null);
        pairs.Select(p => p.Current.AnswerId).Should().Equal("A-01", "A-02");
    }

    [Fact]
    public void Join_ToPrevious_PairsByAnswerId_AndNullPreviousAllNull()
    {
        var cur = Question([Answer("A-01", 10), Answer("A-02", 20)]);
        var prev = Question([Answer("A-02", 22), Answer("A-01", 12)]);

        var paired = GdAnswerJoin.ToPrevious(Aq(cur, crossYear: CrossYearOutcome.Agree, prev: prev));
        paired.Single(p => p.Current.AnswerId == "A-01").Counterpart!.AnchorRow.Should().Be(12);
        paired.Single(p => p.Current.AnswerId == "A-02").Counterpart!.AnchorRow.Should().Be(22);

        var noPrev = GdAnswerJoin.ToPrevious(Aq(cur, crossYear: CrossYearOutcome.Neither, prev: null));
        noPrev.Should().OnlyContain(p => p.Counterpart == null);
    }

    // ── GdPerAnswerEmit (cell address + BL-037 section gate) ──────────────────────

    [Fact]
    public void Emit_CellAddress_IsColumnPlusAnchorRow()
    {
        GdPerAnswerEmit.CellAddress("H", Answer("A-01", 42)).Should().Be("H42");
    }

    [Fact]
    public void SectionGate_AllSections_AdmitsEverything()
    {
        GdPerAnswerEmit.AllSections(Question([Answer("A-01", 10)], section: "G-CO")).Should().BeTrue();
        GdPerAnswerEmit.AllSections(Question([Answer("A-01", 10)], section: "G-ST")).Should().BeTrue();
    }

    [Fact]
    public void SectionGate_SectionsIn_AdmitsOnlyListedSections()
    {
        var gate = GdPerAnswerEmit.SectionsIn(new HashSet<string>(StringComparer.Ordinal) { "G-ST", "G-XX" });

        gate(Question([Answer("A-01", 10)], section: "G-ST")).Should().BeTrue();
        gate(Question([Answer("A-01", 10)], section: "G-CO")).Should().BeFalse();
    }

    // ── GdAnswerFrozenConstraintCell (the reference per-answer check) ─────────────

    [Fact]
    public void RefCheck_OneOfThreeAnswersChangedDv_SingleFindingAtThatAnswerRow()
    {
        // 3 answers; only A-02 (anchor 20) has a DV that diverges from its template counterpart.
        var cur = Question(
        [
            Answer("A-01", 10, dvType: "List", dvFormula: "Yes,No"),
            Answer("A-02", 20, dvType: "List", dvFormula: "Yes,No,Maybe", providedBy: "Unit-B"),
            Answer("A-03", 30, dvType: "Whole", dvOp: "between", dvFormula: "1", dvFormula2: "10"),
        ]);
        var tmpl = Question(
        [
            Answer("A-01", 11, dvType: "List", dvFormula: "No,Yes"),                 // reorder only → unchanged
            Answer("A-02", 21, dvType: "List", dvFormula: "Yes,No"),                 // members differ → CHANGED
            Answer("A-03", 31, dvType: "Whole", dvOp: "between", dvFormula: "1", dvFormula2: "10"), // identical
        ]);
        var check = FrozenCheck();

        var findings = check.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(check));

        var f = findings.Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.FrozenConstraint);
        f.CellAddresses.Should().Be("H20");
        f.CheckResult.Should().Contain("H20");
        f.ProvidedBy.Should().Be("Unit-B");
    }

    [Fact]
    public void RefCheck_MalformedKeyRow_NoFindings()
    {
        var cur = Question([Answer("A-01", 10, dvType: "List", dvFormula: "Yes,No,Maybe")]);
        var check = FrozenCheck();

        var findings = check.Run(
            Result(Aq(cur, withinYear: WithinYearJoin.NotEvaluatedMalformedKey, tmpl: null)),
            Emitter(check));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void RefCheck_AddedInResponse_NoFindings()
    {
        // AddedInResponse → no template side → no per-answer comparison.
        var cur = Question([Answer("A-01", 10, dvType: "Whole", dvOp: "between", dvFormula: "1", dvFormula2: "10")]);
        var check = FrozenCheck();

        var findings = check.Run(
            Result(Aq(cur, withinYear: WithinYearJoin.AddedInResponse, tmpl: null)),
            Emitter(check));

        findings.Should().BeEmpty();
    }
}
