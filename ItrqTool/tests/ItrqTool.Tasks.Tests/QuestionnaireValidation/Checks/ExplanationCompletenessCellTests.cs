using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Checks;

// Coverage for the ExplanationCompletenessCell check (finding 6b): the per-row rule (request I
// present + current K blank → one finding at K{row}), the four I/K combinations, the per-row
// emit on a multi-row question (one finding per offending row), and the input-validity gate
// (NotEvaluatedMalformedKey rows skipped — parity with RequiredInputCellAnyValue). Constructs
// real RlqV01Question records (the check is RLQ-specific) the way CrossYearDeviationCellTests does.
public sealed class ExplanationCompletenessCellTests
{
    private const string Column = "K";

    private static ExplanationCompletenessCell<RlqV01Question> Primitive() =>
        new(q => q.ExplanationRows.Select(r => new ExplanationRowView(r.Requested, r.Current, r.RowNumber, q.ProvidedBy)), Column);

    private static RlqExplanationRow Expl(int rowNumber, string? requested, string? current) =>
        new(Requested: requested, Previous: null, Current: current, RowNumber: rowNumber);

    // Real RlqV01Question carrying just the fields 6b reads (ExplanationRows, QuestionNumber/Text,
    // ProvidedBy); everything else null/empty. The anchor row is the group's first row.
    private static RlqV01Question Q(int anchorRow, params RlqExplanationRow[] rows) =>
        new(
            RowNumber: anchorRow,
            XrefId: "x1",
            OriginalText: "Question text",
            QuestionText: "Question text",
            SectionName: "Section",
            QuestionNumber: anchorRow.ToString(),
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
            ProvidedBy: "Unit-A",
            ExplanationRows: rows);

    private static AlignedQuestion<RlqV01Question> Aq(
        RlqV01Question cur,
        WithinYearJoin within = WithinYearJoin.JoinedByXrefId) =>
        new(Current: cur, WithinYear: within, TemplateMatch: null,
            RowShifted: false, TextMismatched: false,
            CrossYear: CrossYearOutcome.Neither, PreviousMatch: null,
            XrefIdCounterpart: null, MatcherCandidate: null, MatcherBaseScore: null);

    private static AlignmentResult<RlqV01Question> Result(params AlignedQuestion<RlqV01Question>[] rows) =>
        new(rows.ToList(), Array.Empty<RlqV01Question>(), Array.Empty<MalformedKey>());

    private static FindingEmitter Emitter(
        ExplanationCompletenessCell<RlqV01Question> primitive,
        IReadOnlyDictionary<string, FindingEvaluation>? overrides = null) =>
        new(overrides ?? new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(primitive.Descriptors));

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Descriptor_IsExplanationIncomplete_ErrorMissingResponse()
    {
        var d = Primitive().Descriptors.Should().ContainSingle().Subject;
        d.Id.Should().Be("input-cell.explanation.incomplete");
        d.Check.Should().Be(ValidationCheck.MissingResponse);
        d.DefaultEvaluation.Should().Be(FindingEvaluation.Error);
        d.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RequestPresent_CurrentBlank_EmitsOneFindingAtK_Error()
    {
        var p = Primitive();
        var q = Q(8, Expl(8, requested: "Please explain", current: null));

        var f = p.Run(Result(Aq(q)), Emitter(p)).Should().ContainSingle().Subject;

        f.Check.Should().Be(ValidationCheck.MissingResponse);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("K8");
        f.ProvidedBy.Should().Be("Unit-A");
        f.RequestedData.Should().Be("Please explain");
        f.CheckResult.Should().Contain("K8");
    }

    [Fact]
    public void RequestPresent_CurrentPresent_NoFinding()
    {
        var p = Primitive();
        var q = Q(8, Expl(8, requested: "Please explain", current: "Here is the answer"));
        p.Run(Result(Aq(q)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void RequestBlank_CurrentBlank_NoFinding()
    {
        // No request → no requirement, even though the current explanation is blank.
        var p = Primitive();
        var q = Q(8, Expl(8, requested: null, current: null));
        p.Run(Result(Aq(q)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void RequestBlank_CurrentPresent_NoFinding()
    {
        // No request → nothing to complete; a volunteered current explanation is fine.
        var p = Primitive();
        var q = Q(8, Expl(8, requested: "   ", current: "Volunteered"));
        p.Run(Result(Aq(q)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void MultiRowQuestion_TwoOffendingRows_EmitsTwoFindingsAtTheirRows()
    {
        // Q3-style multi-row question (rows 8/9/10): rows 8 and 10 have a request with a blank
        // current; row 9 is complete. Proves PER-ROW emission — exactly two findings at K8 and K10.
        var p = Primitive();
        var q = Q(8,
            Expl(8,  requested: "explain a", current: null),
            Expl(9,  requested: "explain b", current: "done b"),
            Expl(10, requested: "explain c", current: "   "));

        var findings = p.Run(Result(Aq(q)), Emitter(p));

        findings.Should().HaveCount(2);
        findings.Select(f => f.CellAddresses).Should().Equal("K8", "K10");
        findings.Should().OnlyContain(f => f.Evaluation == FindingEvaluation.Error);
    }

    [Fact]
    public void MalformedKeyRow_Skipped_EvenWhenIncomplete()
    {
        // Mirrors RequiredInputCellAnyValue's gate: malformed-key rows are covered by the
        // structure sweep, so dependent input checks skip them entirely.
        var p = Primitive();
        var q = Q(8, Expl(8, requested: "Please explain", current: null));
        p.Run(Result(Aq(q, within: WithinYearJoin.NotEvaluatedMalformedKey)), Emitter(p))
            .Should().BeEmpty();
    }

    [Fact]
    public void SeverityOverride_Applies()
    {
        var p = Primitive();
        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            ["input-cell.explanation.incomplete"] = FindingEvaluation.Warning,
        };
        var q = Q(8, Expl(8, requested: "Please explain", current: null));
        p.Run(Result(Aq(q)), Emitter(p, overrides))
            .Should().ContainSingle().Which.Evaluation.Should().Be(FindingEvaluation.Warning);
    }
}
