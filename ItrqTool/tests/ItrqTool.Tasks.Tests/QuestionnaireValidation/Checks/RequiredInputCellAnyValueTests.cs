using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Checks;

// E1 coverage: the RequiredInputCellAnyValue<T> primitive — presence-only (any non-blank
// value accepted, no allowed-set). One role-templated descriptor, fires only on
// IsNullOrWhiteSpace, skips malformed-key rows. Sheet-agnostic: uses a minimal local
// IAlignmentIdentity record; no dependency on the Clq/Rlq harnesses.
public sealed class RequiredInputCellAnyValueTests
{
    // ── Local test fixtures (no Clq/Rlq-harness dependency) ─────────────────────

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

    // Role/column used across all tests — a *new* column, not "answer" (that role belongs
    // to the baseline in CLQ). We test with the RLQ material-change role/column shape.
    private const string Role = "material-change";
    private const string Column = "L";

    private static RequiredInputCellAnyValue<CheckTestQuestion> Primitive() =>
        new(q => q.Value, q => q.ProvidedBy, Role, Column);

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
        RequiredInputCellAnyValue<CheckTestQuestion> primitive,
        IReadOnlyDictionary<string, FindingEvaluation>? overrides = null) =>
        new(
            overrides ?? new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(primitive.Descriptors));

    // ── Tests ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Descriptors_IsOneRoleTemplatedFinding()
    {
        var primitive = Primitive();

        primitive.Descriptors.Should().ContainSingle();

        var missing = primitive.Descriptors[0];
        missing.Id.Should().Be("input-cell.material-change.missing");
        missing.DefaultEvaluation.Should().Be(FindingEvaluation.Error);
        missing.Check.Should().Be(ValidationCheck.MissingResponse);
        missing.Description.Should().NotBeNullOrWhiteSpace();
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
        f.CellAddresses.Should().Be("L7");
        f.CheckResult.Should().Contain("L7").And.Contain("is empty");
    }

    [Fact]
    public void WhitespaceValue_TreatedAsBlank()
    {
        var primitive = Primitive();
        var findings = primitive.Run(Align(new CheckTestQuestion(7, "  ")), Emitter(primitive));

        var f = findings.Should().ContainSingle().Subject;
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("L7");
    }

    [Fact]
    public void PopulatedValue_NoFinding()
    {
        var primitive = Primitive();
        // Any non-blank value is accepted — no allowed-set constraint.
        var findings = primitive.Run(Align(new CheckTestQuestion(3, "No")), Emitter(primitive));

        findings.Should().BeEmpty();
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
        var findings = primitive.Run(
            Align(new CheckTestQuestion(7, Value: null, ProvidedBy: "Unit-A")),
            Emitter(primitive));

        findings.Should().ContainSingle().Which.ProvidedBy.Should().Be("Unit-A");
    }

    [Fact]
    public void SeverityOverride_Applies()
    {
        var primitive = Primitive();
        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            ["input-cell.material-change.missing"] = FindingEvaluation.Warning,
        };
        var findings = primitive.Run(Align(new CheckTestQuestion(7, Value: null)), Emitter(primitive, overrides));

        findings.Should().ContainSingle().Which.Evaluation.Should().Be(FindingEvaluation.Warning);
    }

    [Fact]
    public void UnknownIdNotInCatalogue_Throws()
    {
        var primitive = Primitive();
        // An emitter whose catalogue lacks this primitive's id — emitting fails loud.
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
            new CheckTestQuestion(10, null),     // blank → missing
            new CheckTestQuestion(11, "Yes"),    // populated → no finding
            new CheckTestQuestion(12, null)),    // blank → missing
            Emitter(primitive));

        findings.Should().HaveCount(2);
        findings[0].CellAddresses.Should().Be("L10");
        findings[1].CellAddresses.Should().Be("L12");
    }
}
