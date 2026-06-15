using FluentAssertions;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Config;

public sealed class ConfigLoaderTests
{
    // ── Minimal test config shape ──────────────────────────────────────────────

    private sealed class VesselConfig
    {
        public string HullMaterial { get; init; } = "";
        public int DisplacementTonnes { get; init; }
        public string Classification { get; init; } = "";
    }

    private static IReadOnlyList<string> ValidateVessel(VesselConfig config)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(config.HullMaterial))
            errors.Add("HullMaterial must not be empty.");
        if (config.DisplacementTonnes <= 0)
            errors.Add("DisplacementTonnes must be greater than 0.");
        return errors;
    }

    private const string ValidJson = """
        {
          "hullMaterial": "steel",
          "displacementTonnes": 4500,
          "classification": "Bulk carrier"
        }
        """;

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public void Load_ValidJson_ReturnsConfig()
    {
        var config = ConfigLoader.Load<VesselConfig>(ValidJson, ValidateVessel);

        config.HullMaterial.Should().Be("steel");
        config.DisplacementTonnes.Should().Be(4500);
        config.Classification.Should().Be("Bulk carrier");
    }

    // ── JSON-level failures ────────────────────────────────────────────────────

    [Fact]
    public void Load_UnknownJsonProperty_ThrowsConfigException_CouldNotBeParsed()
    {
        var json = """
            {
              "hullMaterial": "steel",
              "displacementTonnes": 3000,
              "bogusUncharted": true
            }
            """;

        var act = () => ConfigLoader.Load<VesselConfig>(json, ValidateVessel);

        act.Should().Throw<ConfigException>()
            .Which.Message.Should().Contain("could not be parsed");
    }

    [Fact]
    public void Load_MalformedJson_ThrowsConfigException_CouldNotBeParsed()
    {
        var act = () => ConfigLoader.Load<VesselConfig>("{ not valid json", ValidateVessel);

        act.Should().Throw<ConfigException>()
            .Which.Message.Should().Contain("could not be parsed");
    }

    [Fact]
    public void Load_LiteralNull_ThrowsConfigException_NullMessage()
    {
        var act = () => ConfigLoader.Load<VesselConfig>("null", ValidateVessel);

        act.Should().Throw<ConfigException>()
            .Which.Message.Should().Contain("null");
    }

    // ── Semantic failures: validate delegate errors aggregated ─────────────────

    [Fact]
    public void Load_ValidateDelegateSingleError_ThrowsConfigException_NamingField()
    {
        var json = """
            {
              "hullMaterial": "",
              "displacementTonnes": 4500
            }
            """;

        var act = () => ConfigLoader.Load<VesselConfig>(json, ValidateVessel);

        act.Should().Throw<ConfigException>()
            .Which.Message.Should().Contain("HullMaterial");
    }

    [Fact]
    public void Load_ValidateDelegateMultipleErrors_AllAggregatedIntoOneException()
    {
        var json = """
            {
              "hullMaterial": "",
              "displacementTonnes": 0
            }
            """;

        var act = () => ConfigLoader.Load<VesselConfig>(json, ValidateVessel);

        act.Should().Throw<ConfigException>()
            .Which.Message
            .Should().Contain("HullMaterial")
            .And.Contain("DisplacementTonnes");
    }

    [Fact]
    public void Load_ValidateDelegateNoErrors_DoesNotThrow()
    {
        var act = () => ConfigLoader.Load<VesselConfig>(ValidJson, _ => []);

        act.Should().NotThrow();
    }
}
