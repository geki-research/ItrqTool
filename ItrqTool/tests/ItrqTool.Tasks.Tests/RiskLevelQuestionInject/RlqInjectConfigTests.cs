using FluentAssertions;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.RiskLevelQuestionInject;
using Xunit;

namespace ItrqTool.Tasks.Tests.RiskLevelQuestionInject;

public sealed class RlqInjectConfigTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Solution root (.slnx) not found above test output directory.");
    }

    private static RlqInjectConfig ValidConfig() => new()
    {
        CurrentConfigFilename = "rlq-v02-validation-config.json",
        PreviousConfigFilename = "rlq-v01-validation-config.json"
    };

    // ── production asset ──────────────────────────────────────────────────────

    [Fact]
    public void ProductionAsset_LoadsAndValidates()
    {
        var json = File.ReadAllText(Path.Combine(SolutionRoot(), "configs", "rlq-inject-config.json"));

        var config = ConfigLoader.Load<RlqInjectConfig>(json, c => c.Validate());

        config.CurrentConfigFilename.Should().Be("rlq-v02-validation-config.json");
        config.PreviousConfigFilename.Should().Be("rlq-v01-validation-config.json");
        config.Validate().Should().BeEmpty();
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
        var c = new RlqInjectConfig
        {
            CurrentConfigFilename = "",
            PreviousConfigFilename = "rlq-v01-validation-config.json"
        };
        c.Validate().Should().Contain(e => e.Contains("CurrentConfigFilename"));
    }

    // ── PreviousConfigFilename ────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyPreviousConfigFilename_ReturnsError()
    {
        var c = new RlqInjectConfig
        {
            CurrentConfigFilename = "rlq-v02-validation-config.json",
            PreviousConfigFilename = ""
        };
        c.Validate().Should().Contain(e => e.Contains("PreviousConfigFilename"));
    }

    // ── multi-error collection ────────────────────────────────────────────────

    [Fact]
    public void Validate_BothMissing_CollectsBothErrors()
    {
        var c = new RlqInjectConfig
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
