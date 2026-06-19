using FluentAssertions;
using ItrqTool.Tasks.ControlLevelQuestionInject;
using ItrqTool.Tasks.ControlLevelQuestionValidationV01;
using ItrqTool.Tasks.ControlLevelQuestionValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using Xunit;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionInject;

public sealed class ClqInjectConfigTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Solution root (.slnx) not found above test output directory.");
    }

    private static ClqInjectConfig ValidConfig() => new()
    {
        CurrentConfigFilename = "clq-v01-validation-config.json",
        PreviousConfigFilename = "clq-v02-validation-config.json",
        CarryForwardEnabled = true,
        StabilityTriggerToken = "No",
        ExplanationMergeSeparator = "\n",
        ExplanationStrengthsPrefix = "Strengths:\n",
        ExplanationWeaknessesPrefix = "Weaknesses:\n"
    };

    // ── production asset ──────────────────────────────────────────────────────

    [Fact]
    public void ProductionAsset_LoadsAndValidates()
    {
        var json = File.ReadAllText(Path.Combine(SolutionRoot(), "configs", "clq-inject-config.json"));

        var config = ConfigLoader.Load<ClqInjectConfig>(json, c => c.Validate());

        config.CurrentConfigFilename.Should().Be("clq-v01-validation-config.json");
        config.PreviousConfigFilename.Should().Be("clq-v02-validation-config.json");
        config.CarryForwardEnabled.Should().BeTrue();
        config.StabilityTriggerToken.Should().Be("No");
        config.ExplanationMergeSeparator.Should().Be("\n");
        config.ExplanationStrengthsPrefix.Should().Be("Strengths:\n");
        config.ExplanationWeaknessesPrefix.Should().Be("Weaknesses:\n");
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
        var c = new ClqInjectConfig
        {
            CurrentConfigFilename = "",
            PreviousConfigFilename = "clq-v02-validation-config.json",
            CarryForwardEnabled = true,
            StabilityTriggerToken = "No",
            ExplanationMergeSeparator = "\n",
            ExplanationStrengthsPrefix = "Strengths:\n",
            ExplanationWeaknessesPrefix = "Weaknesses:\n"
        };
        c.Validate().Should().Contain(e => e.Contains("CurrentConfigFilename"));
    }

    // ── PreviousConfigFilename ────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyPreviousConfigFilename_ReturnsError()
    {
        var c = new ClqInjectConfig
        {
            CurrentConfigFilename = "clq-v01-validation-config.json",
            PreviousConfigFilename = "",
            CarryForwardEnabled = true,
            StabilityTriggerToken = "No",
            ExplanationMergeSeparator = "\n",
            ExplanationStrengthsPrefix = "Strengths:\n",
            ExplanationWeaknessesPrefix = "Weaknesses:\n"
        };
        c.Validate().Should().Contain(e => e.Contains("PreviousConfigFilename"));
    }

    // ── StabilityTriggerToken gate ────────────────────────────────────────────

    [Fact]
    public void Validate_CarryForwardEnabled_EmptyStabilityTriggerToken_ReturnsError()
    {
        var c = new ClqInjectConfig
        {
            CurrentConfigFilename = "clq-v01-validation-config.json",
            PreviousConfigFilename = "clq-v02-validation-config.json",
            CarryForwardEnabled = true,
            StabilityTriggerToken = "",
            ExplanationMergeSeparator = "\n",
            ExplanationStrengthsPrefix = "Strengths:\n",
            ExplanationWeaknessesPrefix = "Weaknesses:\n"
        };
        c.Validate().Should().Contain(e => e.Contains("StabilityTriggerToken"));
    }

    [Fact]
    public void Validate_CarryForwardDisabled_EmptyStabilityTriggerToken_ReturnsNoError()
    {
        var c = new ClqInjectConfig
        {
            CurrentConfigFilename = "clq-v01-validation-config.json",
            PreviousConfigFilename = "clq-v02-validation-config.json",
            CarryForwardEnabled = false,
            StabilityTriggerToken = "",
            ExplanationMergeSeparator = "\n",
            ExplanationStrengthsPrefix = "Strengths:\n",
            ExplanationWeaknessesPrefix = "Weaknesses:\n"
        };
        c.Validate().Should().BeEmpty();
    }

    // ── multi-error collection ────────────────────────────────────────────────

    [Fact]
    public void Validate_MultipleErrors_AllCollected()
    {
        var c = new ClqInjectConfig
        {
            CurrentConfigFilename = "",
            PreviousConfigFilename = "",
            CarryForwardEnabled = true,
            StabilityTriggerToken = ""
        };
        var errors = c.Validate();
        errors.Should().HaveCountGreaterThanOrEqualTo(3);
        errors.Should().Contain(e => e.Contains("CurrentConfigFilename"));
        errors.Should().Contain(e => e.Contains("PreviousConfigFilename"));
        errors.Should().Contain(e => e.Contains("StabilityTriggerToken"));
    }

    // ── G column mapping (inert — no validation output change) ────────────────

    [Fact]
    public void V01Config_PreviousExplanationColumn_IsG()
    {
        var json = File.ReadAllText(Path.Combine(SolutionRoot(), "configs", "clq-v01-validation-config.json"));
        var config = ConfigLoader.Load<ClqV01Config>(json, c => c.Validate());
        config.PreviousExplanationColumn.Should().Be("G");
    }

    [Fact]
    public void V02Config_PreviousExplanationColumn_IsG()
    {
        var json = File.ReadAllText(Path.Combine(SolutionRoot(), "configs", "clq-v02-validation-config.json"));
        var config = ConfigLoader.Load<ControlLevelQuestionValidationV02Config>(json, c => c.Validate());
        config.PreviousExplanationColumn.Should().Be("G");
    }
}
