using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Checks;

// E2 coverage: the FrozenConstraintCell<T> primitive — role-templated descriptor,
// the JoinedByXrefId-only gate (template required), DV comparison via DvComparer
// (template-first/current-second), List-type member-order invariance, AddedInResponse
// and malformed-key skips, severity-override flow, and the fail-loud KeyNotFoundException
// contract. Self-contained: a minimal local IAlignmentIdentity record and a tiny
// AlignmentResult builder — no dependency on the Clq harness.
public sealed class FrozenConstraintCellTests
{
    // ── Local test fixtures (no Clq-harness dependency) ──────────────────────────

    private sealed record DvTestQuestion(
        int RowNumber,
        string? DvType,
        string? DvOp,
        string? DvFormula,
        string? DvFormula2,
        string? ProvidedBy = null,
        string? XrefId = "X1",
        string OriginalText = "orig",
        string QuestionText = "What?",
        string SectionName = "Section",
        string? QuestionNumber = "1") : IAlignmentIdentity;

    // Role/column used across the tests (a *new* column, never "answer").
    private const string Role = "answer-stability";
    private const string Column = "K";

    private static FrozenConstraintCell<DvTestQuestion> Primitive(
        FindingEvaluation ruleChangedDefault = FindingEvaluation.Error) =>
        new(q => q.DvType, q => q.DvOp, q => q.DvFormula, q => q.DvFormula2,
            q => q.ProvidedBy, Role, Column, ruleChangedDefault);

    // Builds an AlignedQuestion; default withinYear=JoinedByXrefId with a template match.
    private static AlignedQuestion<DvTestQuestion> Aq(
        DvTestQuestion cur,
        WithinYearJoin withinYear = WithinYearJoin.JoinedByXrefId,
        DvTestQuestion? tmpl = null) =>
        new(Current: cur,
            WithinYear: withinYear,
            TemplateMatch: tmpl,
            RowShifted: false,
            TextMismatched: false,
            CrossYear: CrossYearOutcome.Neither,
            PreviousMatch: null,
            XrefIdCounterpart: null,
            MatcherCandidate: null,
            MatcherBaseScore: null);

    private static AlignmentResult<DvTestQuestion> Result(
        params AlignedQuestion<DvTestQuestion>[] rows) =>
        new(rows.ToList(),
            Array.Empty<DvTestQuestion>(),
            Array.Empty<MalformedKey>());

    private static FindingEmitter Emitter(
        FrozenConstraintCell<DvTestQuestion> primitive,
        IReadOnlyDictionary<string, FindingEvaluation>? overrides = null) =>
        new(
            overrides ?? new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(primitive.Descriptors));

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Descriptor_IsRoleTemplatedRuleChanged()
    {
        var primitive = Primitive();

        primitive.Descriptors.Should().HaveCount(1);

        var d = primitive.Descriptors.Single();
        d.Id.Should().Be("constraint.answer-stability.validation-rule-changed");
        d.DefaultEvaluation.Should().Be(FindingEvaluation.Error);
        d.Check.Should().Be(ValidationCheck.FrozenConstraint);
        d.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void DvUnchanged_NoFinding()
    {
        var tmpl = new DvTestQuestion(5, "Whole", "between", "1", "10");
        var cur  = new DvTestQuestion(5, "Whole", "between", "1", "10");
        var primitive = Primitive();

        var findings = primitive.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(primitive));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DvTypeChanged_EmitsRuleChanged()
    {
        var tmpl = new DvTestQuestion(7, "List", null, "Yes,No", null);
        var cur  = new DvTestQuestion(7, "Whole", null, null, null);
        var primitive = Primitive();

        var findings = primitive.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(primitive));

        var f = findings.Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.FrozenConstraint);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("K7");
        f.CheckResult.Should().Contain("K7");
    }

    [Theory]
    [InlineData("Whole", "between",    "1", "10",  "Whole", "greaterThan", "1", "10")]  // op differs
    [InlineData("Whole", "between",    "1", "10",  "Whole", "between",     "2", "10")]  // formula differs
    [InlineData("Whole", "between",    "1", "10",  "Whole", "between",     "1", "20")]  // formula2 differs
    public void OperatorOrFormulaChanged_Emits(
        string? tmplType, string? tmplOp, string? tmplF1, string? tmplF2,
        string? curType,  string? curOp,  string? curF1,  string? curF2)
    {
        var tmpl = new DvTestQuestion(5, tmplType, tmplOp, tmplF1, tmplF2);
        var cur  = new DvTestQuestion(5, curType,  curOp,  curF1,  curF2);
        var primitive = Primitive();

        var findings = primitive.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(primitive));

        findings.Should().ContainSingle().Which.Check.Should().Be(ValidationCheck.FrozenConstraint);
    }

    [Fact]
    public void BothSidesNullDv_NoFinding()
    {
        var tmpl = new DvTestQuestion(3, null, null, null, null);
        var cur  = new DvTestQuestion(3, null, null, null, null);
        var primitive = Primitive();

        var findings = primitive.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(primitive));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void ListType_ReorderedMembers_NoFinding()
    {
        var tmpl = new DvTestQuestion(4, "List", null, "Yes,No", null);
        var cur  = new DvTestQuestion(4, "List", null, "No,Yes", null);
        var primitive = Primitive();

        var findings = primitive.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(primitive));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void ListType_DifferentMembers_Emits()
    {
        var tmpl = new DvTestQuestion(6, "List", null, "Yes,No", null);
        var cur  = new DvTestQuestion(6, "List", null, "Yes,No,Maybe", null);
        var primitive = Primitive();

        var findings = primitive.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(primitive));

        findings.Should().ContainSingle().Which.Check.Should().Be(ValidationCheck.FrozenConstraint);
    }

    [Fact]
    public void AddedInResponse_Skipped()
    {
        // cur has DV; no template because row is AddedInResponse → skipped
        var cur = new DvTestQuestion(8, "Whole", "between", "1", "10");
        var primitive = Primitive();

        var findings = primitive.Run(
            Result(Aq(cur, WithinYearJoin.AddedInResponse, tmpl: null)),
            Emitter(primitive));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void MalformedRow_Skipped()
    {
        var cur = new DvTestQuestion(9, "Whole", "between", "1", "10");
        var primitive = Primitive();

        var findings = primitive.Run(
            Result(Aq(cur, WithinYearJoin.NotEvaluatedMalformedKey, tmpl: null)),
            Emitter(primitive));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void ProvidedBy_FlowsThrough()
    {
        var tmpl = new DvTestQuestion(5, "Whole", "between", "1", "10");
        var cur  = new DvTestQuestion(5, "Whole", "greaterThan", "1", "10", ProvidedBy: "Unit-B");
        var primitive = Primitive();

        var findings = primitive.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(primitive));

        findings.Should().ContainSingle().Which.ProvidedBy.Should().Be("Unit-B");
    }

    [Fact]
    public void SeverityOverride_Applies()
    {
        var tmpl = new DvTestQuestion(5, "Whole", "between", "1", "10");
        var cur  = new DvTestQuestion(5, "Whole", "greaterThan", "1", "10");
        var primitive = Primitive();
        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            ["constraint.answer-stability.validation-rule-changed"] = FindingEvaluation.Warning,
        };

        var findings = primitive.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(primitive, overrides));

        findings.Should().ContainSingle().Which.Evaluation.Should().Be(FindingEvaluation.Warning);
    }

    [Fact]
    public void UnknownIdNotInCatalogue_Throws()
    {
        var tmpl = new DvTestQuestion(5, "Whole", "between", "1", "10");
        var cur  = new DvTestQuestion(5, "Whole", "greaterThan", "1", "10");
        var primitive = Primitive();
        var foreignDescriptor = new FindingDescriptor(
            "some.other.id", FindingEvaluation.Information, ValidationCheck.Structure, "x");
        var emitter = new FindingEmitter(
            new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(new[] { foreignDescriptor }));

        var act = () => primitive.Run(Result(Aq(cur, tmpl: tmpl)), emitter);
        act.Should().Throw<KeyNotFoundException>();
    }
}
