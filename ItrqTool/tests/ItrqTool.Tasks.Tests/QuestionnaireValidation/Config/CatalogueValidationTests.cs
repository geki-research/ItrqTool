using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Config;

public sealed class CatalogueValidationTests
{
    // ── Two-descriptor catalogue with clearly distinct ids ─────────────────────

    private static FindingDescriptor Windmill() => new(
        "mechanical.windmill-blade-cracked",
        FindingEvaluation.Error,
        ValidationCheck.Structure,
        "A windmill blade developed a stress fracture beyond the permissible crack-growth threshold.");

    private static FindingDescriptor Telescope() => new(
        "optical.telescope-mirror-deformed",
        FindingEvaluation.Warning,
        ValidationCheck.FrozenValue,
        "The primary mirror surface deviated from the diffraction-limited curvature specification.");

    private static FindingCatalogue TwoCatalogue() =>
        new(new[] { Windmill(), Telescope() });

    // ── Known keys ────────────────────────────────────────────────────────────

    [Fact]
    public void UnknownOverrideKeys_AllKnown_ReturnsEmpty()
    {
        var overrides = new Dictionary<string, FindingEvaluation>
        {
            ["mechanical.windmill-blade-cracked"] = FindingEvaluation.Warning,
            ["optical.telescope-mirror-deformed"] = FindingEvaluation.Error,
        };

        var result = CatalogueValidation.UnknownOverrideKeys(overrides, TwoCatalogue());

        result.Should().BeEmpty();
    }

    // ── Unknown keys ──────────────────────────────────────────────────────────

    [Fact]
    public void UnknownOverrideKeys_SingleUnknown_ReturnsThatKey()
    {
        var overrides = new Dictionary<string, FindingEvaluation>
        {
            ["mechanical.windmill-blade-cracked"] = FindingEvaluation.Warning,
            ["botanical.fern-spore-dispersal"] = FindingEvaluation.Error,
        };

        var result = CatalogueValidation.UnknownOverrideKeys(overrides, TwoCatalogue());

        result.Should().ContainSingle().Which.Should().Be("botanical.fern-spore-dispersal");
    }

    [Fact]
    public void UnknownOverrideKeys_AllUnknown_ReturnsAll()
    {
        var overrides = new Dictionary<string, FindingEvaluation>
        {
            ["celestial.pulsar-timing-drift"] = FindingEvaluation.Information,
            ["maritime.tidal-gauge-offset"] = FindingEvaluation.Warning,
        };

        var result = CatalogueValidation.UnknownOverrideKeys(overrides, TwoCatalogue());

        result.Should().HaveCount(2)
            .And.Contain("celestial.pulsar-timing-drift")
            .And.Contain("maritime.tidal-gauge-offset");
    }

    // ── Empty overrides ────────────────────────────────────────────────────────

    [Fact]
    public void UnknownOverrideKeys_EmptyOverrides_ReturnsEmpty()
    {
        var result = CatalogueValidation.UnknownOverrideKeys(
            new Dictionary<string, FindingEvaluation>(),
            TwoCatalogue());

        result.Should().BeEmpty();
    }
}
