using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Checks;

// Coverage for ConfiguredTriggerInDvList<T>: two-descriptor shape (trigger-not-in-dv-list /
// dv-list-unresolvable, both ConfigConsistency Fatal), ONCE cardinality with representative-list
// selection, ordinal-trim membership (parity with DvConformanceEvaluator List branch),
// case-sensitivity, the no-longer-silent dv-list-unresolvable case (anyTemplateAligned + all-null),
// deliberate-silent case (no JoinedByXrefId at all), multiple-missing-triggers, and severity-override
// flow for both descriptors. Shape mirrors DvConformanceCellTests: self-contained local record + tiny
// builders. No Clq-harness dependency.
public sealed class ConfiguredTriggerInDvListTests
{
    private sealed record Q(
        int RowNumber,
        IReadOnlyList<string>? DvListValues,
        string? XrefId = "X1",
        string OriginalText = "orig",
        string QuestionText = "What?",
        string SectionName = "Section",
        string? QuestionNumber = "1") : IAlignmentIdentity;

    // Config-style fields: trigger set and role are never inline literals at the call site under test.
    private static readonly IReadOnlyList<string> DefaultTriggers = new[] { "Yes" };
    private const string Role          = "material-change-explanation";
    private const string TriggerColumn = "L";

    private static ConfiguredTriggerInDvList<Q> Primitive(
        IReadOnlyList<string>? triggers = null,
        FindingEvaluation notInListDefault = FindingEvaluation.Fatal,
        FindingEvaluation unresolvableDefault = FindingEvaluation.Fatal) =>
        new(q => q.DvListValues,
            Role,
            TriggerColumn,
            triggers ?? DefaultTriggers,
            notInListDefault,
            unresolvableDefault);

    private static AlignedQuestion<Q> Aq(
        Q cur,
        WithinYearJoin withinYear = WithinYearJoin.JoinedByXrefId,
        Q? tmpl = null) =>
        new(Current: cur, WithinYear: withinYear, TemplateMatch: tmpl,
            RowShifted: false, TextMismatched: false,
            CrossYear: CrossYearOutcome.Neither, PreviousMatch: null,
            XrefIdCounterpart: null, MatcherCandidate: null, MatcherBaseScore: null);

    private static AlignmentResult<Q> Result(params AlignedQuestion<Q>[] rows) =>
        new(rows.ToList(), Array.Empty<Q>(), Array.Empty<MalformedKey>());

    private static FindingEmitter Emitter(
        ConfiguredTriggerInDvList<Q> primitive,
        IReadOnlyDictionary<string, FindingEvaluation>? overrides = null) =>
        new(overrides ?? new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(primitive.Descriptors));

    // Template question with a DV list (or null).
    private static Q Tmpl(IReadOnlyList<string>? list) =>
        new(RowNumber: 1, DvListValues: list);

    // Current question (DvListValues irrelevant — primitive reads from TemplateMatch).
    private static Q Cur(int row = 5) =>
        new(RowNumber: row, DvListValues: null);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Descriptors_Shape()
    {
        var p = Primitive();
        p.Descriptors.Should().HaveCount(2);

        var notInList = p.Descriptors[0];
        notInList.Id.Should().Be($"config.{Role}.trigger-not-in-dv-list");
        notInList.Check.Should().Be(ValidationCheck.ConfigConsistency);
        notInList.DefaultEvaluation.Should().Be(FindingEvaluation.Fatal);
        notInList.Description.Should().NotBeNullOrWhiteSpace();

        var unresolvable = p.Descriptors[1];
        unresolvable.Id.Should().Be($"config.{Role}.dv-list-unresolvable");
        unresolvable.Check.Should().Be(ValidationCheck.ConfigConsistency);
        unresolvable.DefaultEvaluation.Should().Be(FindingEvaluation.Fatal);
        unresolvable.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TriggerInDvList_NoFinding()
    {
        // Configured trigger "Yes" IS a member of ["Yes","No"] → no finding.
        var tmpl = Tmpl(new[] { "Yes", "No" });
        var p    = Primitive(triggers: new[] { "Yes" });
        p.Run(Result(Aq(Cur(), tmpl: tmpl)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void TriggerNotInDvList_OneFatal()
    {
        // Configured trigger "Yes" is NOT a member of ["A","B"] → one Fatal trigger-not-in-dv-list.
        var tmpl     = Tmpl(new[] { "A", "B" });
        var triggers = new[] { "Yes" };
        var p        = Primitive(triggers: triggers);

        var f = p.Run(Result(Aq(Cur(), tmpl: tmpl)), Emitter(p)).Should().ContainSingle().Subject;

        f.Check.Should().Be(ValidationCheck.ConfigConsistency);
        f.Evaluation.Should().Be(FindingEvaluation.Fatal);
        f.CellAddresses.Should().Be(TriggerColumn); // column-only, no row
        f.QuestionNumber.Should().BeNull();
        f.QuestionText.Should().BeNull();
        f.ProvidedBy.Should().BeNull();
        f.CheckResult.Should().Contain("Yes");
        f.CheckResult.Should().Contain("not a member");
    }

    [Fact]
    public void Membership_TrimBoth_NoFinding()
    {
        // List has " No " (with spaces); trigger " Yes " (with spaces) → trim-both Ordinal → member.
        var tmpl     = Tmpl(new[] { " Yes ", " No " });
        var triggers = new[] { " Yes " };
        var p        = Primitive(triggers: triggers);
        p.Run(Result(Aq(Cur(), tmpl: tmpl)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void Membership_CaseSensitive_OneFatal()
    {
        // List has "yes" (lowercase); configured trigger "Yes" (uppercase) → Ordinal → NOT a member.
        var tmpl     = Tmpl(new[] { "yes" });
        var triggers = new[] { "Yes" };
        var p        = Primitive(triggers: triggers);

        var f = p.Run(Result(Aq(Cur(), tmpl: tmpl)), Emitter(p)).Should().ContainSingle().Subject;

        f.Check.Should().Be(ValidationCheck.ConfigConsistency);
        f.Evaluation.Should().Be(FindingEvaluation.Fatal);
        f.CheckResult.Should().Contain("Yes");
        f.CheckResult.Should().Contain("not a member");
    }

    [Fact]
    public void DvListUnresolvable_TemplateAligned_OneFatal()
    {
        // JoinedByXrefId question exists but its template DV list is null → dv-list-unresolvable Fatal.
        // (No-longer-silent case: anyTemplateAligned = true but repList = null.)
        var tmpl = Tmpl(list: null);
        var p    = Primitive();

        var f = p.Run(Result(Aq(Cur(), tmpl: tmpl)), Emitter(p)).Should().ContainSingle().Subject;

        f.Check.Should().Be(ValidationCheck.ConfigConsistency);
        f.Evaluation.Should().Be(FindingEvaluation.Fatal);
        f.CellAddresses.Should().Be(TriggerColumn);
        f.QuestionNumber.Should().BeNull();
        f.QuestionText.Should().BeNull();
        f.ProvidedBy.Should().BeNull();
        f.CheckResult.Should().Contain("could not be resolved");
    }

    [Fact]
    public void NoTemplateAlignment_NoFinding()
    {
        // Only AddedInResponse / malformed rows (NO JoinedByXrefId) → no vocabulary to check; silent.
        var cur = Cur();
        var p   = Primitive();
        p.Run(Result(
            Aq(cur, WithinYearJoin.AddedInResponse, tmpl: null),
            Aq(cur, WithinYearJoin.NotEvaluatedMalformedKey, tmpl: null)),
            Emitter(p))
            .Should().BeEmpty();
    }

    [Fact]
    public void MultipleMissingTriggers_OneFatalEach()
    {
        // List ["Yes","No"], triggers ["Maybe","Other"] → two Fatal trigger-not-in-dv-list.
        var tmpl     = Tmpl(new[] { "Yes", "No" });
        var triggers = new[] { "Maybe", "Other" };
        var p        = Primitive(triggers: triggers);

        var findings = p.Run(Result(Aq(Cur(), tmpl: tmpl)), Emitter(p));

        findings.Should().HaveCount(2);
        findings.Should().AllSatisfy(f =>
        {
            f.Check.Should().Be(ValidationCheck.ConfigConsistency);
            f.Evaluation.Should().Be(FindingEvaluation.Fatal);
            f.CellAddresses.Should().Be(TriggerColumn);
        });
        findings.Select(f => f.CheckResult).Should().Contain(r => r.Contains("Maybe"));
        findings.Select(f => f.CheckResult).Should().Contain(r => r.Contains("Other"));
    }

    [Fact]
    public void RepresentativeList_FirstResolvedWins()
    {
        // First JoinedByXrefId has ["Yes","No"] (trigger is member); a later one has null.
        // → representative = first resolved list → no trigger-not-in-dv-list, no dv-list-unresolvable.
        var tmplFirst  = Tmpl(new[] { "Yes", "No" });
        var tmplSecond = Tmpl(list: null);
        var p          = Primitive(triggers: new[] { "Yes" });

        p.Run(Result(
            Aq(Cur(5), tmpl: tmplFirst),
            Aq(Cur(6), tmpl: tmplSecond)),
            Emitter(p))
            .Should().BeEmpty();
    }

    [Fact]
    public void SeverityOverride_TriggerNotInList()
    {
        // Override trigger-not-in-dv-list to Error → emitted finding's Evaluation is Error.
        var tmpl     = Tmpl(new[] { "A", "B" });
        var triggers = new[] { "Yes" };
        var p        = Primitive(triggers: triggers);
        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            [$"config.{Role}.trigger-not-in-dv-list"] = FindingEvaluation.Error,
        };

        p.Run(Result(Aq(Cur(), tmpl: tmpl)), Emitter(p, overrides))
            .Should().ContainSingle()
            .Which.Evaluation.Should().Be(FindingEvaluation.Error);
    }

    [Fact]
    public void SeverityOverride_Unresolvable()
    {
        // Override dv-list-unresolvable to Warning → the emitted finding's Evaluation is Warning.
        var tmpl = Tmpl(list: null); // template-aligned but null list → unresolvable path
        var p    = Primitive();
        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            [$"config.{Role}.dv-list-unresolvable"] = FindingEvaluation.Warning,
        };

        p.Run(Result(Aq(Cur(), tmpl: tmpl)), Emitter(p, overrides))
            .Should().ContainSingle()
            .Which.Evaluation.Should().Be(FindingEvaluation.Warning);
    }

    [Fact]
    public void EmptyTriggerSet_ResolvedList_NoFinding()
    {
        // No configured triggers → nothing to check → no findings even with a resolved list.
        var tmpl     = Tmpl(new[] { "Yes", "No" });
        var triggers = Array.Empty<string>();
        var p        = Primitive(triggers: triggers);
        p.Run(Result(Aq(Cur(), tmpl: tmpl)), Emitter(p)).Should().BeEmpty();
    }
}
