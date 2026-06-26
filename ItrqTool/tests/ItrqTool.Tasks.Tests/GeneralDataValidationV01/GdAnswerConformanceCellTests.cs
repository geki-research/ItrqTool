using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.GeneralDataValidationV01;

// Tests for GdAnswerConformanceCell.
// Assertions: Check + CellAddress (+ CheckResult substring), never id or total count (lesson 112).
public sealed class GdAnswerConformanceCellTests
{
    // ── Builders ────────────────────────────────────────────────────────────────

    private static GdAnswer Answer(
        string? value, int anchorRow,
        string? dvType = null, string? dvOp = null, string? dvFormula = null,
        string? dvFormula2 = null, IReadOnlyList<string>? listValues = null,
        string? provided = null) =>
        new(AnswerId: "A-01", AnchorRow: anchorRow,
            PreviousAnswer: null, Answer: value, MaterialChange: value,
            ProvidedBy: provided, Explanations: [],
            AnswerDvType: dvType, AnswerDvFormula: dvFormula,
            AnswerDvOperator: dvOp, AnswerDvFormula2: dvFormula2,
            AnswerDvListValues: listValues,
            MaterialChangeDvType: dvType, MaterialChangeDvFormula: dvFormula,
            MaterialChangeDvOperator: dvOp, MaterialChangeDvFormula2: dvFormula2,
            MaterialChangeDvListValues: listValues);

    private static GdV01Question Question(IReadOnlyList<GdAnswer> answers, int row = 10) =>
        new(RowNumber: row, XrefId: "Q1", OriginalText: "orig", QuestionText: "What?",
            SectionName: "G-ST", QuestionNumber: "1", Answers: answers);

    private static AlignedQuestion<GdV01Question> Aq(GdV01Question cur) =>
        new(Current: cur, WithinYear: WithinYearJoin.JoinedByXrefId, TemplateMatch: null,
            RowShifted: false, TextMismatched: false,
            CrossYear: CrossYearOutcome.Neither, PreviousMatch: null,
            XrefIdCounterpart: null, MatcherCandidate: null, MatcherBaseScore: null);

    private static AlignmentResult<GdV01Question> Result(params AlignedQuestion<GdV01Question>[] rows) =>
        new(rows.ToList(), Array.Empty<GdV01Question>(), Array.Empty<MalformedKey>());

    private static GdAnswerConformanceCell HCheck() =>
        new(a => a.Answer,
            a => a.AnswerDvType, a => a.AnswerDvOperator, a => a.AnswerDvFormula, a => a.AnswerDvFormula2,
            a => a.AnswerDvListValues, a => a.ProvidedBy,
            role: "answer", column: "H");

    private static GdAnswerConformanceCell LCheck() =>
        new(a => a.MaterialChange,
            a => a.MaterialChangeDvType, a => a.MaterialChangeDvOperator,
            a => a.MaterialChangeDvFormula, a => a.MaterialChangeDvFormula2,
            a => a.MaterialChangeDvListValues, a => a.ProvidedBy,
            role: "material-change", column: "L");

    private static FindingEmitter Emitter(GdAnswerConformanceCell check) =>
        new(new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(check.Descriptors));

    // ── H: not-conformant ───────────────────────────────────────────────────────

    [Fact]
    public void H_ViolatesInlineListDv_NotConformantFinding()
    {
        // Answer "Maybe" is not in the inline list "Yes,No".
        var cur = Question([Answer("Maybe", 10, dvType: "List", dvFormula: "\"Yes,No\"",
                                  listValues: ["Yes", "No"], provided: "Unit-A")]);
        var check = HCheck();
        var findings = check.Run(Result(Aq(cur)), Emitter(check));

        var f = findings.Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.InputConformance);
        f.CellAddresses.Should().Be("H10");
        f.CheckResult.Should().Contain("Maybe");
        f.ProvidedBy.Should().Be("Unit-A");
    }

    [Fact]
    public void H_ConformantValue_NoFinding()
    {
        var cur = Question([Answer("Yes", 10, dvType: "List", dvFormula: "\"Yes,No\"",
                                  listValues: ["Yes", "No"])]);
        var check = HCheck();
        check.Run(Result(Aq(cur)), Emitter(check)).Should().BeEmpty();
    }

    [Fact]
    public void H_ListDvWithNullResolvedValues_UnresolvableFinding()
    {
        // List DV but AnswerDvListValues is null (range-ref not yet resolved or unresolvable).
        var cur = Question([Answer("Yes", 10, dvType: "List",
                                  dvFormula: "Lists!$A$1:$A$2", listValues: null, provided: "Unit-B")]);
        var check = HCheck();
        var findings = check.Run(Result(Aq(cur)), Emitter(check));

        var f = findings.Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.InputConformance);
        f.CellAddresses.Should().Be("H10");
        f.CheckResult.Should().Contain("could not be resolved");
        f.ProvidedBy.Should().Be("Unit-B");
    }

    [Fact]
    public void H_NoDv_NotCheckable_NoFinding()
    {
        var cur = Question([Answer("Anything", 10, dvType: null)]);
        var check = HCheck();
        check.Run(Result(Aq(cur)), Emitter(check)).Should().BeEmpty();
    }

    [Fact]
    public void H_BlankValue_NoFinding_PresenceIsRequiredInputsConcern()
    {
        var cur = Question([Answer(null, 10, dvType: "List", dvFormula: "\"Yes,No\"",
                                  listValues: ["Yes", "No"])]);
        var check = HCheck();
        check.Run(Result(Aq(cur)), Emitter(check)).Should().BeEmpty();
    }

    // ── L: material-change role ─────────────────────────────────────────────────

    [Fact]
    public void L_ViolatesInlineListDv_NotConformantFindingAtLColumn()
    {
        var cur = Question([Answer("Maybe", 10, dvType: "List", dvFormula: "\"Y,N\"",
                                  listValues: ["Y", "N"])]);
        var check = LCheck();
        var findings = check.Run(Result(Aq(cur)), Emitter(check));

        var f = findings.Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.InputConformance);
        f.CellAddresses.Should().Be("L10");
    }

    [Fact]
    public void L_UnresolvableList_UnresolvableFinding()
    {
        var cur = Question([Answer("Y", 10, dvType: "List",
                                  dvFormula: "=YesNoOptions", listValues: null)]);
        var check = LCheck();
        var findings = check.Run(Result(Aq(cur)), Emitter(check));

        findings.Should().ContainSingle().Which.Check.Should().Be(ValidationCheck.InputConformance);
        findings[0].CheckResult.Should().Contain("could not be resolved");
    }

    [Fact]
    public void L_ConformantValue_NoFinding()
    {
        var cur = Question([Answer("Y", 10, dvType: "List", dvFormula: "\"Y,N\"",
                                  listValues: ["Y", "N"])]);
        var check = LCheck();
        check.Run(Result(Aq(cur)), Emitter(check)).Should().BeEmpty();
    }
}
