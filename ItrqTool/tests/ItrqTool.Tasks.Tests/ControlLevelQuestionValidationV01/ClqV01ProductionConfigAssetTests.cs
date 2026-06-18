using FluentAssertions;
using ItrqTool.Tasks.ControlLevelQuestionValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using Xunit;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidationV01;

/// <summary>
/// Pins the CLQ_v01 production config asset (configs/clq-v01-validation-config.json), whose
/// ChapterRows is a STRING array. Mirrors ClqV02ProductionConfigAssetTests. Asserts the v01
/// column map (provided-by M / xref-id N, no K), the 7-string ChapterRows, the 30 SectionRows,
/// and that LayoutParser derives exactly 30 sections.
/// </summary>
public sealed class ClqV01ProductionConfigAssetTests
{
    private static string ConfigAssetPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Solution root (.slnx) not found above test output directory.");
        return Path.Combine(dir.FullName, "configs", "clq-v01-validation-config.json");
    }

    [Fact]
    public void ProductionConfig_LoadsWithNoValidationErrors()
    {
        var json = File.ReadAllText(ConfigAssetPath());

        var config = ConfigLoader.Load<ClqV01Config>(json, c => c.Validate());

        config.Validate().Should().BeEmpty();

        config.SheetName.Should().Be("IT Risk Control Self-Assessment");

        config.TextColumn.Should().Be("D");
        config.GuidanceColumn.Should().Be("E");
        config.PreviousAnswerColumn.Should().Be("F");
        config.AnswerColumn.Should().Be("H");
        config.StrengthsColumn.Should().Be("I");
        config.WeaknessesColumn.Should().Be("J");
        config.ProvidedByColumn.Should().Be("M");
        config.XrefIdColumn.Should().Be("N");

        config.ChapterRows.Should().Equal("4", "51", "86", "145", "177", "191", "205");

        config.SectionRows.Should().Equal(
            "5:6-13",   "14:15-24",  "25:26-39",  "40:41-45",  "46:47-50",
            "52:53-57", "58:59-66",  "67:68-71",  "72:73-74",  "75:76-79",  "80:81-85",
            "87:88-100",  "101:102-106", "107:108-114", "115:116-127", "128:129-134",
            "135:136-139", "140:141-144",
            "146:147-150", "151:152-159", "160:161-166", "167:168-176",
            "178:179-185", "186:187-190",
            "192:193-199", "200:201-204",
            "206:207-208", "209:210-221", "222:223-228", "229:230-233");

        config.AllowedAnswers.Should().Equal("1", "2", "3", "4");
        config.DeviationThreshold.Should().Be(2);
        config.SeverityOverrides.Should().BeEmpty();

        LayoutParser.Parse(
                config.ChapterRows, config.SectionRows,
                config.TextColumn, config.TextColumn, config.TextColumn)
            .Sections.Should().HaveCount(30,
                "the v01 production config's SectionRows must parse to exactly 30 sections");
    }
}
