using FluentAssertions;
using Xunit;
using ItrqTool.Tasks.TemplateDiff;

namespace ItrqTool.Tasks.Tests;

public sealed class ControlLevelQuestionsConfigTests
{
    [Fact]
    public void ParsedSections_EmptySectionRows_ReturnsEmptyList()
    {
        var cfg = new ControlLevelQuestionsConfig();
        cfg.ParsedSections.Should().BeEmpty();
    }

    [Fact]
    public void ParsedSections_ValidEntries_ParsesAllCorrectly()
    {
        var cfg = new ControlLevelQuestionsConfig
        {
            SectionRows = ["2:3-5", "10:11-11", "20:25-30"]
        };

        var sections = cfg.ParsedSections;

        sections.Should().HaveCount(3);
        sections[0].Should().Be(new SectionDefinition(2, 3, 5));
        sections[1].Should().Be(new SectionDefinition(10, 11, 11));
        sections[2].Should().Be(new SectionDefinition(20, 25, 30));
    }

    [Theory]
    [InlineData("notvalid")]       // no colon
    [InlineData(":3-5")]           // empty sectionRow part (colonIdx == 0)
    [InlineData("2:notdash")]      // no dash after colon
    [InlineData("2:3")]            // no dash
    public void ParsedSections_MissingColonOrDash_ThrowsFormatException(string entry)
    {
        var cfg = new ControlLevelQuestionsConfig { SectionRows = [entry] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("0:3-5")]      // sectionRow == 0
    [InlineData("-1:3-5")]     // sectionRow negative
    [InlineData("abc:3-5")]    // sectionRow not an integer
    public void ParsedSections_InvalidSectionRow_ThrowsFormatException(string entry)
    {
        var cfg = new ControlLevelQuestionsConfig { SectionRows = [entry] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>().WithMessage("*sectionRow*");
    }

    [Theory]
    [InlineData("2:0-5")]      // firstQuestionRow == 0
    [InlineData("2:abc-5")]    // firstQuestionRow not an integer
    public void ParsedSections_InvalidFirstQuestionRow_ThrowsFormatException(string entry)
    {
        var cfg = new ControlLevelQuestionsConfig { SectionRows = [entry] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>().WithMessage("*firstQuestionRow*");
    }

    [Theory]
    [InlineData("2:3-0")]      // lastQuestionRow == 0
    [InlineData("2:3-abc")]    // lastQuestionRow not an integer
    public void ParsedSections_InvalidLastQuestionRow_ThrowsFormatException(string entry)
    {
        var cfg = new ControlLevelQuestionsConfig { SectionRows = [entry] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>().WithMessage("*lastQuestionRow*");
    }

    [Theory]
    [InlineData("2:2-5")]   // firstQuestionRow == sectionRow
    [InlineData("2:1-5")]   // firstQuestionRow < sectionRow
    public void ParsedSections_FirstRowNotGreaterThanSectionRow_ThrowsFormatException(string entry)
    {
        var cfg = new ControlLevelQuestionsConfig { SectionRows = [entry] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>().WithMessage("*firstQuestionRow*greater*");
    }

    [Fact]
    public void ParsedSections_LastRowLessThanFirstRow_ThrowsFormatException()
    {
        var cfg = new ControlLevelQuestionsConfig { SectionRows = ["2:5-4"] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>().WithMessage("*lastQuestionRow*");
    }

    [Fact]
    public void ParsedSections_SingleQuestionRow_IsValid()
    {
        var cfg = new ControlLevelQuestionsConfig { SectionRows = ["3:4-4"] };
        var sections = cfg.ParsedSections;
        sections.Should().ContainSingle()
            .Which.Should().Be(new SectionDefinition(3, 4, 4));
    }
}
