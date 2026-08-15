using FluentAssertions;
using ItrqTool.Tasks.GeneralDataInject;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using Xunit;

namespace ItrqTool.Tasks.Tests.GeneralDataInject;

public sealed class GdInjectConfigTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Solution root (.slnx) not found above test output directory.");
    }

    private static GdInjectConfig ValidConfig() => new()
    {
        CurrentConfigFilename = "gd-v02-validation-config.json",
        PreviousConfigFilename = "gd-v01-validation-config.json"
    };

    // ── production asset ──────────────────────────────────────────────────────

    [Fact]
    public void ProductionAsset_LoadsAndValidates()
    {
        var json = File.ReadAllText(Path.Combine(SolutionRoot(), "configs", "gd-inject-config.json"));

        var config = ConfigLoader.Load<GdInjectConfig>(json, c => c.Validate());

        config.CurrentConfigFilename.Should().Be("gd-v02-validation-config.json");
        config.PreviousConfigFilename.Should().Be("gd-v01-validation-config.json");
        config.Validate().Should().BeEmpty();
    }

    // The production asset deliberately omits the key, so it exercises the default path.
    [Fact]
    public void ProductionAsset_OmitsThreshold_TakesDefaultOfHalf()
    {
        var json = File.ReadAllText(Path.Combine(SolutionRoot(), "configs", "gd-inject-config.json"));

        var config = ConfigLoader.Load<GdInjectConfig>(json, c => c.Validate());

        config.QidJoinSimilarityThreshold.Should().Be(0.50);
    }

    // ── QidJoinSimilarityThreshold — default ──────────────────────────────────

    [Fact]
    public void DefaultConstant_IsHalf()
    {
        GdInjectConfig.DefaultQidJoinSimilarityThreshold.Should().Be(0.50);
    }

    [Fact]
    public void QidJoinSimilarityThreshold_NotSet_DefaultsToHalf()
    {
        ValidConfig().QidJoinSimilarityThreshold.Should().Be(0.50);
    }

    [Fact]
    public void QidJoinSimilarityThreshold_AbsentFromJson_DefaultsToHalf()
    {
        const string json = """
            {
              "CurrentConfigFilename": "gd-v02-validation-config.json",
              "PreviousConfigFilename": "gd-v01-validation-config.json"
            }
            """;

        var config = ConfigLoader.Load<GdInjectConfig>(json, c => c.Validate());

        config.QidJoinSimilarityThreshold.Should().Be(0.50);
    }

    // ── QidJoinSimilarityThreshold — supplied value wins ──────────────────────

    [Fact]
    public void QidJoinSimilarityThreshold_PresentInJson_OverridesDefault()
    {
        const string json = """
            {
              "CurrentConfigFilename": "gd-v02-validation-config.json",
              "PreviousConfigFilename": "gd-v01-validation-config.json",
              "QidJoinSimilarityThreshold": 0.85
            }
            """;

        var config = ConfigLoader.Load<GdInjectConfig>(json, c => c.Validate());

        config.QidJoinSimilarityThreshold.Should().Be(0.85);
    }

    // ── QidJoinSimilarityThreshold — range validation ─────────────────────────

    [Theory]
    [InlineData(0.0)]    // inclusive lower bound
    [InlineData(0.5)]
    [InlineData(1.0)]    // inclusive upper bound
    public void Validate_ThresholdInRange_ReturnsNoErrors(double threshold)
    {
        var c = new GdInjectConfig
        {
            CurrentConfigFilename = "gd-v02-validation-config.json",
            PreviousConfigFilename = "gd-v01-validation-config.json",
            QidJoinSimilarityThreshold = threshold
        };

        c.Validate().Should().BeEmpty();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void Validate_ThresholdOutOfRange_ReturnsError(double threshold)
    {
        var c = new GdInjectConfig
        {
            CurrentConfigFilename = "gd-v02-validation-config.json",
            PreviousConfigFilename = "gd-v01-validation-config.json",
            QidJoinSimilarityThreshold = threshold
        };

        c.Validate().Should().ContainSingle()
            .Which.Should().Contain("QidJoinSimilarityThreshold");
    }

    // An out-of-range value is a fail-loud config error, not a silent clamp: ConfigLoader
    // aggregates it into a ConfigException, which the task surfaces as Succeeded:false.
    [Fact]
    public void Load_ThresholdOutOfRange_ThrowsConfigException()
    {
        const string json = """
            {
              "CurrentConfigFilename": "gd-v02-validation-config.json",
              "PreviousConfigFilename": "gd-v01-validation-config.json",
              "QidJoinSimilarityThreshold": 1.5
            }
            """;

        var act = () => ConfigLoader.Load<GdInjectConfig>(json, c => c.Validate());

        act.Should().Throw<ConfigException>()
            .WithMessage("*QidJoinSimilarityThreshold*");
    }

    // ── valid config ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_AllValid_ReturnsNoErrors()
    {
        ValidConfig().Validate().Should().BeEmpty();
    }

    // ── CurrentConfigFilename ─────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyCurrentConfigFilename_ReturnsError()
    {
        var c = new GdInjectConfig
        {
            CurrentConfigFilename = "",
            PreviousConfigFilename = "gd-v01-validation-config.json"
        };
        c.Validate().Should().Contain(e => e.Contains("CurrentConfigFilename"));
    }

    // ── PreviousConfigFilename ────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyPreviousConfigFilename_ReturnsError()
    {
        var c = new GdInjectConfig
        {
            CurrentConfigFilename = "gd-v02-validation-config.json",
            PreviousConfigFilename = ""
        };
        c.Validate().Should().Contain(e => e.Contains("PreviousConfigFilename"));
    }

    // ── multi-error collection ────────────────────────────────────────────────

    [Fact]
    public void Validate_BothMissing_CollectsBothErrors()
    {
        var c = new GdInjectConfig
        {
            CurrentConfigFilename = "",
            PreviousConfigFilename = ""
        };
        var errors = c.Validate();
        errors.Should().HaveCount(2);
        errors.Should().Contain(e => e.Contains("CurrentConfigFilename"));
        errors.Should().Contain(e => e.Contains("PreviousConfigFilename"));
    }
}
