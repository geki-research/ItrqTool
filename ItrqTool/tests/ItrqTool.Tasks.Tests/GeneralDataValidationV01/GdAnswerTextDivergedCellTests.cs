using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.GeneralDataValidationV01;

// Tests for GdAnswerTextDivergedCell.
// Assertions: Check + CellAddress (+ CheckResult substring), never id or total count (lesson 112).
public sealed class GdAnswerTextDivergedCellTests
{
    // ── Builders ────────────────────────────────────────────────────────────────

    private static GdAnswer Answer(int anchorRow, string? provided = null) =>
        new(AnswerId: "A-01", AnchorRow: anchorRow,
            PreviousAnswer: null, Answer: null, MaterialChange: null,
            ProvidedBy: provided, Explanations: []);

    private static GdV01Question Question(IReadOnlyList<GdAnswer> answers, int row = 10,
                                          string? xrefId = "Q1") =>
        new(RowNumber: row, XrefId: xrefId, OriginalText: "orig", QuestionText: "What?",
            SectionName: "G-ST", QuestionNumber: "1", Answers: answers);

    private static AlignedQuestion<GdV01Question> AqDiverged(
        GdV01Question cur, GdV01Question? xrefCounterpart = null) =>
        new(Current: cur, WithinYear: WithinYearJoin.JoinedByXrefId, TemplateMatch: null,
            RowShifted: false, TextMismatched: false,
            CrossYear: CrossYearOutcome.SameXrefIdTextDiverged,
            PreviousMatch: null,           // not used as confident baseline for SameXrefIdTextDiverged
            XrefIdCounterpart: xrefCounterpart,
            MatcherCandidate: null, MatcherBaseScore: null);

    private static AlignedQuestion<GdV01Question> AqAgree(GdV01Question cur) =>
        new(Current: cur, WithinYear: WithinYearJoin.JoinedByXrefId, TemplateMatch: null,
            RowShifted: false, TextMismatched: false,
            CrossYear: CrossYearOutcome.Agree, PreviousMatch: cur,
            XrefIdCounterpart: null, MatcherCandidate: null, MatcherBaseScore: null);

    private static AlignmentResult<GdV01Question> Result(params AlignedQuestion<GdV01Question>[] rows) =>
        new(rows.ToList(), Array.Empty<GdV01Question>(), Array.Empty<MalformedKey>());

    private static GdAnswerTextDivergedCell Check() =>
        new(q => q.Answers.FirstOrDefault()?.ProvidedBy, column: "Q");

    private static FindingEmitter Emitter(GdAnswerTextDivergedCell check) =>
        new(new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(check.Descriptors));

    // ── Tests ───────────────────────────────────────────────────────────────────

    [Fact]
    public void SameXrefIdTextDiverged_EmitsOneFindingAtQuestionRow()
    {
        var cur = Question([Answer(20, "Unit-A")], row: 10, xrefId: "Q1");
        var prev = Question([Answer(22)], row: 22, xrefId: "Q1");
        var check = Check();

        var findings = check.Run(Result(AqDiverged(cur, xrefCounterpart: prev)), Emitter(check));

        var f = findings.Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.Structure);
        f.CellAddresses.Should().Be("Q10");
        f.CheckResult.Should().Contain("Q1");
        f.ProvidedBy.Should().Be("Unit-A");
    }

    [Fact]
    public void SameXrefIdTextDiverged_NullXrefCounterpart_StillEmits()
    {
        // XrefIdCounterpart can be null if the previous question was removed.
        var cur = Question([Answer(10)], row: 10, xrefId: "Q5");
        var check = Check();

        var findings = check.Run(Result(AqDiverged(cur, xrefCounterpart: null)), Emitter(check));

        findings.Should().ContainSingle().Which.CellAddresses.Should().Be("Q10");
    }

    [Fact]
    public void Agree_NoFinding()
    {
        var cur = Question([Answer(10)], row: 10);
        var check = Check();

        check.Run(Result(AqAgree(cur)), Emitter(check)).Should().BeEmpty();
    }

    [Fact]
    public void Neither_NoFinding()
    {
        var cur = Question([Answer(10)], row: 10);
        var aqNeither = new AlignedQuestion<GdV01Question>(
            Current: cur, WithinYear: WithinYearJoin.JoinedByXrefId, TemplateMatch: null,
            RowShifted: false, TextMismatched: false,
            CrossYear: CrossYearOutcome.Neither, PreviousMatch: null,
            XrefIdCounterpart: null, MatcherCandidate: null, MatcherBaseScore: null);
        var check = Check();

        check.Run(Result(aqNeither), Emitter(check)).Should().BeEmpty();
    }

    [Fact]
    public void TwoQuestions_OneAgreeOneDiverged_OnlyDivergedFires()
    {
        var qAgree   = Question([Answer(5)], row: 5);
        var qDiverge = Question([Answer(10)], row: 10, xrefId: "Q2");
        var check = Check();

        var findings = check.Run(
            Result(AqAgree(qAgree), AqDiverged(qDiverge)),
            Emitter(check));

        findings.Should().ContainSingle().Which.CellAddresses.Should().Be("Q10");
    }
}
