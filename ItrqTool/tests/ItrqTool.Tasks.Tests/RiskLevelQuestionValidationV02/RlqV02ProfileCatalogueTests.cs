using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;
using Xunit;

namespace ItrqTool.Tasks.Tests.RiskLevelQuestionValidationV02;

public sealed class RlqV02ProfileCatalogueTests
{
    // Assembles the finding-id set exactly as RunFromParsedGated does:
    //   BaselineDescriptors + Extensions.SelectMany(Descriptors) + IdentityGateCheck.Descriptors
    private static HashSet<string> CatalogueIds<T>(ValidationPipelineProfile<T> profile)
        where T : class, IAlignmentIdentity
        => profile.BaselineDescriptors
            .Concat(profile.Extensions.SelectMany(e => e.Descriptors))
            .Concat(profile.IdentityGateCheck?.Descriptors ?? [])
            .Select(d => d.Id)
            .ToHashSet();

    private static Dictionary<string, FindingDescriptor> CatalogueDescriptors<T>(
        ValidationPipelineProfile<T> profile)
        where T : class, IAlignmentIdentity
        => profile.BaselineDescriptors
            .Concat(profile.Extensions.SelectMany(e => e.Descriptors))
            .Concat(profile.IdentityGateCheck?.Descriptors ?? [])
            .ToDictionary(d => d.Id);

    private static RlqV01Config V01Config() => new()
    {
        QuestionNumberColumn = "C",
        TextColumn = "D",
        GuidanceColumn = "E",
        RequestedTypeColumn = "F",
        PreviousAnswerColumn = "G",
        AnswerColumn = "H",
        RequestedExplanationColumn = "I",
        PreviousExplanationColumn = "J",
        CurrentExplanationColumn = "K",
        MaterialChangeColumn = "L",
        ProvidedByColumn = "O",
        XrefIdColumn = "Q",
        SheetName = "IT Risk Level Questions",
        SectionRows = ["2:3-20"],
    };

    private static RlqV02Config V02Config() => new()
    {
        QuestionNumberColumn = "C",
        TextColumn = "D",
        GuidanceColumn = "E",
        RequestedTypeColumn = "F",
        PreviousAnswerColumn = "G",
        AnswerColumn = "H",
        RequestedExplanationColumn = "I",
        PreviousExplanationColumn = "J",
        CurrentExplanationColumn = "K",
        MaterialChangeColumn = "L",
        HowExplanationColumn = "M",
        ProvidedByColumn = "P",
        XrefIdColumn = "R",
        SheetName = "IT Risk Level Questions",
        SectionRows = ["2:3-20"],
        MaterialChangeExplanationTriggers = ["Yes"],
    };

    [Fact]
    public void V02Profile_CatalogueIsExactlyV01Plus3NewIds()
    {
        var v01Ids = CatalogueIds(RlqV01Profile.Build(V01Config()));
        var v02Ids = CatalogueIds(RlqV02Profile.Build(V02Config()));

        var expectedNewIds = new HashSet<string>
        {
            "input-cell.material-change-explanation.conditionally-required-missing",
            "config.material-change-explanation.trigger-not-in-dv-list",
            "config.material-change-explanation.dv-list-unresolvable",
        };

        // Count guards — v01 = 12; v02 = v01 + 3 new = 15.
        v01Ids.Should().HaveCount(12, "v01 catalogue must be exactly 12 ids");
        v02Ids.Should().HaveCount(15, "v02 catalogue must be exactly 15 ids (v01 + 3 new)");

        // v02 ⊇ v01: no v01 id dropped or renamed.
        v01Ids.Except(v02Ids).Should().BeEmpty("v02 must contain every v01 id");

        // Exactly the three new ids were added.
        v02Ids.Except(v01Ids).Should().BeEquivalentTo(expectedNewIds,
            "v02 must add exactly these three new ids and nothing else");
    }

    [Fact]
    public void V02Profile_ThreeNewDescriptors_HaveExpectedCheckAndSeverity()
    {
        var descriptors = CatalogueDescriptors(RlqV02Profile.Build(V02Config()));

        // Rule 1: ConditionalRequirement → Error
        descriptors["input-cell.material-change-explanation.conditionally-required-missing"]
            .Should().Match<FindingDescriptor>(d =>
                d.DefaultEvaluation == FindingEvaluation.Error &&
                d.Check == ValidationCheck.ConditionalRequirement);

        // Rule 2a: trigger not in DV list → Fatal
        descriptors["config.material-change-explanation.trigger-not-in-dv-list"]
            .Should().Match<FindingDescriptor>(d =>
                d.DefaultEvaluation == FindingEvaluation.Fatal &&
                d.Check == ValidationCheck.ConfigConsistency);

        // Rule 2b: DV list unresolvable → Fatal
        descriptors["config.material-change-explanation.dv-list-unresolvable"]
            .Should().Match<FindingDescriptor>(d =>
                d.DefaultEvaluation == FindingEvaluation.Fatal &&
                d.Check == ValidationCheck.ConfigConsistency);
    }
}
