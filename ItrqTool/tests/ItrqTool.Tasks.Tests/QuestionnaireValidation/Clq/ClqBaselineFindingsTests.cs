using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Clq;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Clq;

// Faithful-copy guard: the CLQ baseline descriptor table must carry the exact id
// strings, default evaluations, and checks copied verbatim from CLQ_v01's ClqFindings.
public sealed class ClqBaselineFindingsTests
{
    public static IEnumerable<object[]> ExpectedDescriptors() => new[]
    {
        Row(ClqBaselineFinding.XrefIdEmptyOrDuplicated, "structure.xrefid-empty-or-duplicated", FindingEvaluation.Fatal, ValidationCheck.Structure),
        Row(ClqBaselineFinding.QuestionRemoved, "structure.question-removed", FindingEvaluation.Error, ValidationCheck.Structure),
        Row(ClqBaselineFinding.QuestionAdded, "structure.question-added", FindingEvaluation.Error, ValidationCheck.Structure),
        Row(ClqBaselineFinding.QuestionRowShifted, "structure.question-row-shifted", FindingEvaluation.Error, ValidationCheck.Structure),
        Row(ClqBaselineFinding.NumberFormatUnrecognized, "structure.number-format-unrecognized", FindingEvaluation.Warning, ValidationCheck.Structure),
        Row(ClqBaselineFinding.ReferenceTextAltered, "static-cell.reference-text-altered", FindingEvaluation.Warning, ValidationCheck.FrozenValue),
        Row(ClqBaselineFinding.PreviousAnswerAltered, "static-cell.previous-answer-altered", FindingEvaluation.Warning, ValidationCheck.FrozenValue),
        Row(ClqBaselineFinding.AnswerValidationRuleChanged, "constraint.answer-validation-rule-changed", FindingEvaluation.Error, ValidationCheck.FrozenConstraint),
        Row(ClqBaselineFinding.AnswerMissing, "input-cell.answer-missing", FindingEvaluation.Error, ValidationCheck.MissingResponse),
        Row(ClqBaselineFinding.AnswerNotInAllowedSet, "input-cell.answer-not-in-allowed-set", FindingEvaluation.Fatal, ValidationCheck.MissingResponse),
        Row(ClqBaselineFinding.StrengthsMissing, "input-cell.strengths-missing", FindingEvaluation.Error, ValidationCheck.MissingResponse),
        Row(ClqBaselineFinding.WeaknessesMissing, "input-cell.weaknesses-missing", FindingEvaluation.Error, ValidationCheck.MissingResponse),
        Row(ClqBaselineFinding.XrefIdConflict, "cross-year.xrefid-conflict", FindingEvaluation.Error, ValidationCheck.Structure),
        Row(ClqBaselineFinding.NewXrefIdResemblesPrevious, "cross-year.new-xrefid-resembles-previous", FindingEvaluation.Warning, ValidationCheck.Structure),
        Row(ClqBaselineFinding.SameXrefIdTextDiverged, "cross-year.same-xrefid-text-diverged", FindingEvaluation.Warning, ValidationCheck.Structure),
        Row(ClqBaselineFinding.NoPreviousBaseline, "cross-year.no-previous-baseline", FindingEvaluation.Information, ValidationCheck.Structure),
        Row(ClqBaselineFinding.AnswerDeviation, "cross-year.answer-deviation", FindingEvaluation.Warning, ValidationCheck.Deviation),
        Row(ClqBaselineFinding.PreviousAnswerUnusable, "cross-year.previous-answer-unusable", FindingEvaluation.Information, ValidationCheck.Deviation),
    };

    private static object[] Row(ClqBaselineFinding finding, string id, FindingEvaluation eval, ValidationCheck check) =>
        new object[] { finding, id, eval, check };

    [Theory]
    [MemberData(nameof(ExpectedDescriptors))]
    public void Descriptor_carries_the_exact_id_evaluation_and_check(
        ClqBaselineFinding finding, string expectedId, FindingEvaluation expectedEval, ValidationCheck expectedCheck)
    {
        var descriptor = ClqBaselineFindings.Descriptor(finding);

        descriptor.Id.Should().Be(expectedId);
        descriptor.DefaultEvaluation.Should().Be(expectedEval);
        descriptor.Check.Should().Be(expectedCheck);
        descriptor.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void All_covers_every_enum_member_in_order()
    {
        ClqBaselineFindings.All.Select(d => d.Id).Should().Equal(
            ExpectedDescriptors().Select(r => (string)r[1]));
    }

    [Fact]
    public void There_are_exactly_eighteen_baseline_findings()
    {
        ClqBaselineFindings.All.Should().HaveCount(18);
        Enum.GetValues<ClqBaselineFinding>().Should().HaveCount(18);
    }

    [Fact]
    public void All_ids_are_unique()
    {
        ClqBaselineFindings.All.Select(d => d.Id).Should().OnlyHaveUniqueItems();
    }
}
