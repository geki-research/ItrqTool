using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Checks;

// Coverage for ConditionalRequirement<T>: role-templated descriptor (ConditionalRequirement check),
// trigger-gate (F1: trim-both / Ordinal / case-sensitive; F6: set membership; blank trigger no-fire),
// target-gate (blank → emit, present → no emit), F2 gate (NotEvaluatedMalformedKey skipped;
// AddedInResponse evaluated), severity-override flow.
// Shape mirrors DvConformanceCellTests: self-contained local record + tiny builders. No Clq-harness.
public sealed class ConditionalRequirementTests
{
    private sealed record Q(
        int RowNumber,
        string? TriggerValue,
        string? TargetValue,
        string? ProvidedBy = null,
        string? XrefId = "X1",
        string OriginalText = "orig",
        string QuestionText = "What?",
        string SectionName = "Section",
        string? QuestionNumber = "1") : IAlignmentIdentity;

    // Config-style field: trigger set is never an inline literal at the call site under test.
    private static readonly IReadOnlyList<string> DefaultTriggers = new[] { "Yes" };

    private const string Role          = "explanation";
    private const string TargetColumn  = "K";
    private const string TriggerColumn = "J";

    private static ConditionalRequirement<Q> Primitive(
        IReadOnlyList<string>? triggerValues = null,
        FindingEvaluation def = FindingEvaluation.Error) =>
        new(q => q.TargetValue,
            q => q.TriggerValue,
            q => q.ProvidedBy,
            Role,
            TargetColumn,
            TriggerColumn,
            triggerValues ?? DefaultTriggers,
            def);

    private static AlignedQuestion<Q> Aq(
        Q cur,
        WithinYearJoin withinYear = WithinYearJoin.JoinedByXrefId) =>
        new(Current: cur, WithinYear: withinYear, TemplateMatch: null,
            RowShifted: false, TextMismatched: false,
            CrossYear: CrossYearOutcome.Neither, PreviousMatch: null,
            XrefIdCounterpart: null, MatcherCandidate: null, MatcherBaseScore: null);

    private static AlignmentResult<Q> Result(params AlignedQuestion<Q>[] rows) =>
        new(rows.ToList(), Array.Empty<Q>(), Array.Empty<MalformedKey>());

    private static FindingEmitter Emitter(
        ConditionalRequirement<Q> primitive,
        IReadOnlyDictionary<string, FindingEvaluation>? overrides = null) =>
        new(overrides ?? new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(primitive.Descriptors));

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Descriptor_IsRoleTemplated_ConditionallyRequiredMissing()
    {
        var d = Primitive().Descriptors.Should().ContainSingle().Subject;
        d.Id.Should().Be("input-cell.explanation.conditionally-required-missing");
        d.Check.Should().Be(ValidationCheck.ConditionalRequirement);
        d.DefaultEvaluation.Should().Be(FindingEvaluation.Error);
        d.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TriggerMatches_TargetBlank_Emits()
    {
        var cur = new Q(RowNumber: 7, TriggerValue: "Yes", TargetValue: null, ProvidedBy: "Unit-A");
        var p   = Primitive();

        var f = p.Run(Result(Aq(cur)), Emitter(p)).Should().ContainSingle().Subject;

        f.Check.Should().Be(ValidationCheck.ConditionalRequirement);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("K7");
        f.ProvidedBy.Should().Be("Unit-A");
        f.CheckResult.Should().Contain("K7");
        f.CheckResult.Should().Contain("J7");
    }

    [Fact]
    public void TriggerMatches_TargetPresent_NoFinding()
    {
        var cur = new Q(RowNumber: 7, TriggerValue: "Yes", TargetValue: "some explanation");
        var p   = Primitive();
        p.Run(Result(Aq(cur)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void TriggerNotMatched_TargetBlank_NoFinding()
    {
        var cur = new Q(RowNumber: 7, TriggerValue: "No", TargetValue: null);
        var p   = Primitive();
        p.Run(Result(Aq(cur)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void TriggerBlank_TargetBlank_NoFinding()
    {
        var cur = new Q(RowNumber: 7, TriggerValue: "   ", TargetValue: null);
        var p   = Primitive();
        p.Run(Result(Aq(cur)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void F1_TrimBoth_TriggerWithTrailingSpace_StillEmits()
    {
        // Configured token "Yes"; cell value "Yes " (trailing space). Trim-both → matches.
        var triggers = new[] { "Yes" };
        var cur      = new Q(RowNumber: 8, TriggerValue: "Yes ", TargetValue: null);
        var p        = Primitive(triggerValues: triggers);

        p.Run(Result(Aq(cur)), Emitter(p)).Should().ContainSingle()
            .Which.CellAddresses.Should().Be("K8");
    }

    [Fact]
    public void F1_CaseSensitive_LowercaseTrigger_NoFinding()
    {
        // Configured token "Yes"; cell value "yes" (lowercase). Ordinal → no match.
        var triggers = new[] { "Yes" };
        var cur      = new Q(RowNumber: 8, TriggerValue: "yes", TargetValue: null);
        var p        = Primitive(triggerValues: triggers);

        p.Run(Result(Aq(cur)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void F6_SecondSetMember_Emits()
    {
        // Set ["Yes","True"]; trigger cell "True" (second member) ∧ target blank → emits.
        var triggers = new[] { "Yes", "True" };
        var cur      = new Q(RowNumber: 9, TriggerValue: "True", TargetValue: null);
        var p        = Primitive(triggerValues: triggers);

        p.Run(Result(Aq(cur)), Emitter(p)).Should().ContainSingle()
            .Which.CellAddresses.Should().Be("K9");
    }

    [Fact]
    public void MalformedRow_NoFinding()
    {
        var cur = new Q(RowNumber: 10, TriggerValue: "Yes", TargetValue: null);
        var p   = Primitive();
        p.Run(Result(Aq(cur, WithinYearJoin.NotEvaluatedMalformedKey)), Emitter(p))
            .Should().BeEmpty();
    }

    [Fact]
    public void AddedInResponseRow_StillEvaluated()
    {
        // AddedInResponse row — trigger matches ∧ target blank → emits (no template required).
        var cur = new Q(RowNumber: 11, TriggerValue: "Yes", TargetValue: null);
        var p   = Primitive();

        p.Run(Result(Aq(cur, WithinYearJoin.AddedInResponse)), Emitter(p))
            .Should().ContainSingle()
            .Which.CellAddresses.Should().Be("K11");
    }

    [Fact]
    public void SeverityOverride_Applies()
    {
        var cur = new Q(RowNumber: 7, TriggerValue: "Yes", TargetValue: null);
        var p   = Primitive();
        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            ["input-cell.explanation.conditionally-required-missing"] = FindingEvaluation.Warning,
        };

        p.Run(Result(Aq(cur)), Emitter(p, overrides))
            .Should().ContainSingle()
            .Which.Evaluation.Should().Be(FindingEvaluation.Warning);
    }

    [Fact]
    public void TargetWhitespaceOnly_CountsAsBlank_Emits()
    {
        // Whitespace-only target is blank per IsNullOrWhiteSpace → emits.
        var cur = new Q(RowNumber: 7, TriggerValue: "Yes", TargetValue: "   ");
        var p   = Primitive();

        p.Run(Result(Aq(cur)), Emitter(p)).Should().ContainSingle()
            .Which.CellAddresses.Should().Be("K7");
    }
}
