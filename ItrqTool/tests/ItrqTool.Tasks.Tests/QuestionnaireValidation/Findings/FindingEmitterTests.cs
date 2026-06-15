using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Clq;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Findings;

public sealed class FindingEmitterTests
{
    private static readonly IReadOnlyDictionary<string, FindingEvaluation> NoOverrides =
        new Dictionary<string, FindingEvaluation>();

    private static FindingDescriptor Glacier() => new(
        "terrain.glacier-receded",
        FindingEvaluation.Warning,
        ValidationCheck.Deviation,
        "The glacier terminus receded past the surveyed marker since the prior season.");

    private static FindingCatalogue GlacierCatalogue() => new(new[] { Glacier() });

    [Fact]
    public void Emit_resolves_check_and_default_evaluation_from_the_descriptor()
    {
        var emitter = new FindingEmitter(NoOverrides, GlacierCatalogue());

        var finding = emitter.Emit(
            "terrain.glacier-receded",
            cellAddresses: "B7",
            questionNumber: "4.2",
            questionText: "Measured terminus position",
            requestedData: "survey marker offset",
            providedBy: "field team delta",
            checkResult: "receded by eleven metres");

        finding.Check.Should().Be(ValidationCheck.Deviation);
        finding.Evaluation.Should().Be(FindingEvaluation.Warning);
    }

    [Fact]
    public void Emit_applies_a_severity_override_keyed_by_id()
    {
        var overrides = new Dictionary<string, FindingEvaluation>
        {
            ["terrain.glacier-receded"] = FindingEvaluation.Fatal,
        };
        var emitter = new FindingEmitter(overrides, GlacierCatalogue());

        var finding = emitter.Emit(
            "terrain.glacier-receded", "B7", null, null, null, null, "checked");

        finding.Evaluation.Should().Be(FindingEvaluation.Fatal);
    }

    [Fact]
    public void Emit_passes_every_per_finding_data_field_through()
    {
        var emitter = new FindingEmitter(NoOverrides, GlacierCatalogue());

        var finding = emitter.Emit(
            "terrain.glacier-receded",
            cellAddresses: "M3:M9",
            questionNumber: "12.7",
            questionText: "Catchment runoff estimate",
            requestedData: "annual melt volume",
            providedBy: "hydrology unit zephyr",
            checkResult: "exceeds prior baseline by margin");

        finding.CellAddresses.Should().Be("M3:M9");
        finding.QuestionNumber.Should().Be("12.7");
        finding.QuestionText.Should().Be("Catchment runoff estimate");
        finding.RequestedData.Should().Be("annual melt volume");
        finding.ProvidedBy.Should().Be("hydrology unit zephyr");
        finding.CheckResult.Should().Be("exceeds prior baseline by margin");
    }

    [Fact]
    public void Baseline_enum_and_string_id_entry_points_produce_the_same_finding()
    {
        var emitter = new FindingEmitter(NoOverrides, new FindingCatalogue(ClqBaselineFindings.All));

        var viaEnum = emitter.Emit(
            ClqBaselineFinding.AnswerDeviation,
            cellAddresses: "D14",
            questionNumber: "9.1",
            questionText: "Residual risk rating",
            requestedData: null,
            providedBy: "unit orion",
            checkResult: "shifted from 2 to 4");

        var viaString = emitter.Emit(
            ClqBaselineFindings.Descriptor(ClqBaselineFinding.AnswerDeviation).Id,
            cellAddresses: "D14",
            questionNumber: "9.1",
            questionText: "Residual risk rating",
            requestedData: null,
            providedBy: "unit orion",
            checkResult: "shifted from 2 to 4");

        viaEnum.Should().Be(viaString);
    }
}
