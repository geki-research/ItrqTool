using FluentAssertions;
using ItrqTool.Tasks.GeneralDataValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using Xunit;

namespace ItrqTool.Tasks.Tests.GeneralDataValidationV02;

public sealed class GdV02ProductionConfigAssetTests
{
    private static string ConfigAssetPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Solution root (.slnx) not found above test output directory.");
        return Path.Combine(dir.FullName, "configs", "gd-v02-validation-config.json");
    }

    [Fact]
    public void ProductionConfig_LoadsWithNoValidationErrors()
    {
        var json = File.ReadAllText(ConfigAssetPath());

        var config = ConfigLoader.Load<GdV02Config>(json, c => c.Validate());

        config.Validate().Should().BeEmpty();

        // Column letters (v02: M added, O→P, Q→R)
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
        config.HowExplanationColumn.Should().Be("M");
        config.ProvidedByColumn.Should().Be("P");
        config.XrefIdColumn.Should().Be("R");

        config.SheetName.Should().Be("General Data");
        config.DeviationThreshold.Should().Be(0.25);
        config.SeverityOverrides.Should().BeEmpty();

        // 5 processed sections (General comments deliberately omitted — same geometry as v01)
        config.Sections.Should().HaveCount(5);

        var s1 = config.Sections[0];
        s1.HeaderRow.Should().Be(3);
        s1.FirstDataRow.Should().Be(4);
        s1.LastDataRow.Should().Be(9);
        s1.ExpectedName.Should().Be("Entities in scope/contact details");
        s1.MaterialChangeRequired.Should().BeFalse();

        var s2 = config.Sections[1];
        s2.HeaderRow.Should().Be(10);
        s2.FirstDataRow.Should().Be(11);
        s2.LastDataRow.Should().Be(43);
        s2.ExpectedName.Should().Be("Staff");
        s2.MaterialChangeRequired.Should().BeTrue();

        var s3 = config.Sections[2];
        s3.HeaderRow.Should().Be(44);
        s3.FirstDataRow.Should().Be(45);
        s3.LastDataRow.Should().Be(64);
        s3.ExpectedName.Should().Be("Financials (please refer to the Glossary for this section)");
        s3.MaterialChangeRequired.Should().BeTrue();

        var s4 = config.Sections[3];
        s4.HeaderRow.Should().Be(65);
        s4.FirstDataRow.Should().Be(66);
        s4.LastDataRow.Should().Be(91);
        s4.ExpectedName.Should().Be("Oversight");
        s4.MaterialChangeRequired.Should().BeTrue();

        var s5 = config.Sections[4];
        s5.HeaderRow.Should().Be(92);
        s5.FirstDataRow.Should().Be(93);
        s5.LastDataRow.Should().Be(120);
        s5.ExpectedName.Should().Be("IT Environment");
        s5.MaterialChangeRequired.Should().BeTrue();
    }
}
