using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Checks;

// E1 coverage: the RequiredInputCell<T> primitive — role-templated descriptors,
// the baseline-mirrored input-validity shape (blank → missing/Error, value outside
// the ordinal allowed-set → not-in-set/Fatal, mutually exclusive), the malformed-key
// skip, severity-override flow, the fail-loud KeyNotFoundException contract, and
// per-row ordering. The Checks area is sheet-agnostic, so this test is self-contained:
// a minimal local IAlignmentIdentity record plus a tiny AlignmentResult builder — no
// dependency on the Clq harness.
public sealed class RequiredInputCellTests
{
    // ── Local test fixtures (no Clq-harness dependency) ──────────────────────────

    private sealed record CheckTestQuestion(
        int RowNumber,
        string? Value,
        string? ProvidedBy = null,
        WithinYearJoin WithinYear = WithinYearJoin.AddedInResponse,
        string? XrefId = "X1",
        string OriginalText = "orig",
        string QuestionText = "What?",
        string SectionName = "Section",
        string? QuestionNumber = "1") : IAlignmentIdentity;

    // Role/column/allowed-set used across the tests (a *new* column, never "answer").
    private const string Role = "answer-stability";
    private const string Column = "K";
    private static readonly IReadOnlyList<string> Allowed = new[] { "Yes", "No" };

    private static RequiredInputCell<CheckTestQuestion> Primitive(
        IReadOnlyList<string>? allowed = null) =>
        new(q => q.Value, q => q.ProvidedBy, Role, Column, allowed ?? Allowed);

    private static AlignmentResult<CheckTestQuestion> Align(params CheckTestQuestion[] rows)
    {
        var aligned = rows.Select(q => new AlignedQuestion<CheckTestQuestion>(
            Current: q,
            WithinYear: q.WithinYear,
            TemplateMatch: null,
            RowShifted: false,
            TextMismatched: false,
            CrossYear: CrossYearOutcome.Neither,
            PreviousMatch: null,
            XrefIdCounterpart: null,
            MatcherCandidate: null,
            MatcherBaseScore: null)).ToList();
        return new AlignmentResult<CheckTestQuestion>(
            aligned,
            Array.Empty<CheckTestQuestion>(),
            Array.Empty<MalformedKey>());
    }

    private static FindingEmitter Emitter(
        RequiredInputCell<CheckTestQuestion> primitive,
        IReadOnlyDictionary<string, FindingEvaluation>? overrides = null) =>
        new(
            overrides ?? new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(primitive.Descriptors));

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Descriptors_AreTwoRoleTemplatedFindings()
    {
        var primitive = Primitive();

        primitive.Descriptors.Should().HaveCount(2);

        var missing = primitive.Descriptors.Single(d => d.Id == "input-cell.answer-stability.missing");
        missing.DefaultEvaluation.Should().Be(FindingEvaluation.Error);
        missing.Check.Should().Be(ValidationCheck.MissingResponse);
        missing.Description.Should().NotBeNullOrWhiteSpace();

        var notInSet = primitive.Descriptors.Single(d => d.Id == "input-cell.answer-stability.not-in-allowed-set");
        notInSet.DefaultEvaluation.Should().Be(FindingEvaluation.Fatal);
        notInSet.Check.Should().Be(ValidationCheck.MissingResponse);
        notInSet.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_EmitsMissing(string? value)
    {
        var primitive = Primitive();
        var findings = primitive.Run(Align(new CheckTestQuestion(7, value)), Emitter(primitive));

        var f = findings.Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.MissingResponse);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("K7");
        f.CheckResult.Should().Contain("K7");
    }

    [Fact]
    public void WhitespaceValue_TreatedAsBlank()
    {
        var primitive = Primitive();
        var findings = primitive.Run(Align(new CheckTestQuestion(7, "  ")), Emitter(primitive));

        var f = findings.Should().ContainSingle().Subject;
        // Whitespace → missing, NOT not-in-set.
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("K7");
    }

    [Fact]
    public void NotInAllowed_EmitsNotInSet()
    {
        var primitive = Primitive();
        var findings = primitive.Run(Align(new CheckTestQuestion(9, "Maybe")), Emitter(primitive));

        var f = findings.Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.MissingResponse);
        f.Evaluation.Should().Be(FindingEvaluation.Fatal);
        f.CellAddresses.Should().Be("K9");
        f.CheckResult.Should().Contain("'Maybe'").And.Contain("[Yes, No]");
    }

    [Fact]
    public void InAllowed_NoFinding()
    {
        var primitive = Primitive();
        var findings = primitive.Run(Align(new CheckTestQuestion(3, "Yes")), Emitter(primitive));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void Membership_IsOrdinalCaseSensitive()
    {
        var primitive = Primitive(allowed: new[] { "Yes" });
        var findings = primitive.Run(Align(new CheckTestQuestion(4, "yes")), Emitter(primitive));

        var f = findings.Should().ContainSingle().Subject;
        // Ordinal membership: "yes" != "Yes" → not-in-set.
        f.Evaluation.Should().Be(FindingEvaluation.Fatal);
        f.CheckResult.Should().Contain("'yes'");
    }

    [Fact]
    public void MalformedRow_Skipped()
    {
        var primitive = Primitive();
        var malformed = new CheckTestQuestion(5, Value: null, WithinYear: WithinYearJoin.NotEvaluatedMalformedKey);
        var findings = primitive.Run(Align(malformed), Emitter(primitive));

        // Blank value would normally emit missing — but a malformed-key row is skipped.
        findings.Should().BeEmpty();
    }

    [Fact]
    public void ProvidedBy_FlowsThrough()
    {
        var primitive = Primitive();
        var findings = primitive.Run(Align(new CheckTestQuestion(7, Value: null, ProvidedBy: "Unit-A")), Emitter(primitive));

        findings.Should().ContainSingle().Which.ProvidedBy.Should().Be("Unit-A");
    }

    [Fact]
    public void SeverityOverride_Applies()
    {
        var primitive = Primitive();
        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            ["input-cell.answer-stability.missing"] = FindingEvaluation.Warning,
        };
        var findings = primitive.Run(Align(new CheckTestQuestion(7, Value: null)), Emitter(primitive, overrides));

        findings.Should().ContainSingle().Which.Evaluation.Should().Be(FindingEvaluation.Warning);
    }

    [Fact]
    public void UnknownIdNotInCatalogue_Throws()
    {
        var primitive = Primitive();
        // An emitter whose catalogue lacks this primitive's ids — emitting fails loud.
        var foreignDescriptor = new FindingDescriptor("some.other.id", FindingEvaluation.Information, ValidationCheck.Structure, "x");
        var emitter = new FindingEmitter(
            new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(new[] { foreignDescriptor }));

        var act = () => primitive.Run(Align(new CheckTestQuestion(7, Value: null)), emitter);
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void MultipleRows_PerRowFindings_OrderPreserved()
    {
        var primitive = Primitive();
        var findings = primitive.Run(Align(
            new CheckTestQuestion(10, null),      // missing
            new CheckTestQuestion(11, "Yes"),     // valid → no finding
            new CheckTestQuestion(12, "Maybe")),  // not-in-set
            Emitter(primitive));

        findings.Should().HaveCount(2);
        findings[0].CellAddresses.Should().Be("K10");
        findings[0].Evaluation.Should().Be(FindingEvaluation.Error);
        findings[1].CellAddresses.Should().Be("K12");
        findings[1].Evaluation.Should().Be(FindingEvaluation.Fatal);
    }
}
