using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Findings;

public sealed class FindingCatalogueTests
{
    // Distinct, low-similarity ids/descriptions so nothing collides by accident (lesson 73 hygiene).
    private static FindingDescriptor Apple() => new(
        "fruit.apple-bruised",
        FindingEvaluation.Warning,
        ValidationCheck.FrozenValue,
        "The apple shipment arrived bruised beyond the acceptable handling tolerance.");

    private static FindingDescriptor Bicycle() => new(
        "vehicle.bicycle-chain-slipped",
        FindingEvaluation.Error,
        ValidationCheck.Structure,
        "The bicycle drivetrain dropped its chain under load on the third gear.");

    private static FindingDescriptor Comet() => new(
        "astro.comet-tail-divergence",
        FindingEvaluation.Information,
        ValidationCheck.Deviation,
        "The comet's ion tail diverged from the expected solar-wind heading.");

    private static FindingCatalogue ThreeItemCatalogue() =>
        new(new[] { Apple(), Bicycle(), Comet() });

    [Fact]
    public void IsValidId_returns_true_for_present_id()
    {
        ThreeItemCatalogue().IsValidId("vehicle.bicycle-chain-slipped").Should().BeTrue();
    }

    [Fact]
    public void IsValidId_returns_false_for_absent_id()
    {
        ThreeItemCatalogue().IsValidId("fruit.apple-pristine").Should().BeFalse();
    }

    [Fact]
    public void Descriptor_returns_the_matching_descriptor()
    {
        ThreeItemCatalogue().Descriptor("astro.comet-tail-divergence").Should().Be(Comet());
    }

    [Fact]
    public void Descriptor_throws_for_unknown_id()
    {
        var act = () => ThreeItemCatalogue().Descriptor("astro.nebula-missing");
        act.Should().Throw<KeyNotFoundException>().WithMessage("*astro.nebula-missing*");
    }

    [Fact]
    public void AllIds_returns_every_id_in_construction_order()
    {
        ThreeItemCatalogue().AllIds.Should().Equal(
            "fruit.apple-bruised",
            "vehicle.bicycle-chain-slipped",
            "astro.comet-tail-divergence");
    }

    [Fact]
    public void Construction_rejects_a_duplicate_id()
    {
        var duplicate = new FindingDescriptor(
            "fruit.apple-bruised",
            FindingEvaluation.Fatal,
            ValidationCheck.MissingResponse,
            "A second descriptor reusing an id already taken.");

        var act = () => new FindingCatalogue(new[] { Apple(), duplicate });

        act.Should().Throw<ArgumentException>().WithMessage("*fruit.apple-bruised*");
    }
}
