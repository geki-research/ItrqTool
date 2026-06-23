using FluentAssertions;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using Xunit;

namespace ItrqTool.Tasks.Tests.RiskLevelQuestionValidationV01;

public sealed class RlqV01ProductionConfigAssetTests
{
    private static string ConfigAssetPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Solution root (.slnx) not found above test output directory.");
        return Path.Combine(dir.FullName, "configs", "rlq-v01-validation-config.json");
    }

    [Fact]
    public void ProductionConfig_LoadsWithNoValidationErrors()
    {
        var json = File.ReadAllText(ConfigAssetPath());

        var config = ConfigLoader.Load<RlqV01Config>(json, c => c.Validate());

        config.Validate().Should().BeEmpty();

        config.SheetName.Should().Be("IT Risk Level Questions");

        config.QuestionNumberColumn.Should().Be("C");
        config.TextColumn.Should().Be("D");
        config.GuidanceColumn.Should().Be("E");
        config.RequestedTypeColumn.Should().Be("F");
        config.PreviousAnswerColumn.Should().Be("G");
        config.AnswerColumn.Should().Be("H");
        config.RequestedExplanationColumn.Should().Be("I");
        config.PreviousExplanationColumn.Should().Be("J");
        config.CurrentExplanationColumn.Should().Be("K");
        config.MaterialChangeColumn.Should().Be("L");
        config.ProvidedByColumn.Should().Be("O");
        config.XrefIdColumn.Should().Be("Q");

        config.SectionRows.Should().Equal("3:4-21", "22:23-42", "43:44-58", "59:60-71");

        config.DeviationThreshold.Should().Be(0.25);

        config.SeverityOverrides.Should().BeEmpty();

        LayoutParser.Parse(
                [], config.SectionRows,
                config.TextColumn, config.TextColumn, config.TextColumn)
            .Sections.Should().HaveCount(4,
                "the production config's SectionRows must parse to exactly 4 sections");
    }
}
