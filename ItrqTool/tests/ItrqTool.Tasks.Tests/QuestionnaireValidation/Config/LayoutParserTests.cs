using FluentAssertions;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Config;

public sealed class LayoutParserTests
{
    // ── Happy-path ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_MultiChapterMultiSection_ProducesCorrectLayout()
    {
        var chapterRows = new[] { "2", "20" };
        var sectionRows = new[] { "3:4-10", "21:22-30" };

        var layout = LayoutParser.Parse(chapterRows, sectionRows, "C", "D", "E");

        layout.QuestionTextColumn.Should().Be("E");

        layout.Chapters.Should().HaveCount(2);
        layout.Chapters[0].Should().Be(new LayoutChapter(2, "C"));
        layout.Chapters[1].Should().Be(new LayoutChapter(20, "C"));

        layout.Sections.Should().HaveCount(2);
        layout.Sections[0].Should().Be(new LayoutSection(3, 4, 10, "D"));
        layout.Sections[1].Should().Be(new LayoutSection(21, 22, 30, "D"));
    }

    [Fact]
    public void Parse_SingleRowSpanSection_Parses()
    {
        var layout = LayoutParser.Parse([], ["5:6-6"], "C", "C", "C");

        layout.Sections.Should().ContainSingle()
            .Which.Should().Be(new LayoutSection(5, 6, 6, "C"));
    }

    [Fact]
    public void Parse_EmptyChaptersAndSections_ReturnsEmptyLayout()
    {
        var layout = LayoutParser.Parse([], [], "A", "A", "A");

        layout.Chapters.Should().BeEmpty();
        layout.Sections.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NameColumnsAndQuestionTextColumnPopulatedFromArguments()
    {
        var layout = LayoutParser.Parse(["1"], ["2:3-5"], "B", "C", "D");

        layout.Chapters[0].NameColumn.Should().Be("B");
        layout.Sections[0].NameColumn.Should().Be("C");
        layout.QuestionTextColumn.Should().Be("D");
    }

    // ── Chapter row: malformed entries ─────────────────────────────────────────

    [Fact]
    public void Parse_ChapterRow_NonIntegerString_ThrowsFormatException()
    {
        var act = () => LayoutParser.Parse(["reef-formation"], [], "C", "C", "C");

        act.Should().Throw<FormatException>()
            .Which.Message.Should().Contain("reef-formation");
    }

    [Fact]
    public void Parse_ChapterRow_Zero_ThrowsFormatException()
    {
        var act = () => LayoutParser.Parse(["0"], [], "C", "C", "C");

        act.Should().Throw<FormatException>()
            .Which.Message.Should().Contain("0");
    }

    [Fact]
    public void Parse_ChapterRow_Negative_ThrowsFormatException()
    {
        var act = () => LayoutParser.Parse(["-3"], [], "C", "C", "C");

        act.Should().Throw<FormatException>()
            .Which.Message.Should().Contain("-3");
    }

    // ── Section row: malformed entries ─────────────────────────────────────────

    [Fact]
    public void Parse_SectionRow_MissingColon_ThrowsFormatException()
    {
        var act = () => LayoutParser.Parse([], ["badformat"], "C", "C", "C");

        act.Should().Throw<FormatException>()
            .Which.Message.Should().Contain("<sectionRow>:<first>-<last>");
    }

    [Fact]
    public void Parse_SectionRow_MissingDash_ThrowsFormatException()
    {
        var act = () => LayoutParser.Parse([], ["5:6"], "C", "C", "C");

        act.Should().Throw<FormatException>()
            .Which.Message.Should().Contain("<sectionRow>:<first>-<last>");
    }

    [Fact]
    public void Parse_SectionRow_FirstEqualToSectionRow_ThrowsFormatException()
    {
        var act = () => LayoutParser.Parse([], ["5:5-10"], "C", "C", "C");

        act.Should().Throw<FormatException>()
            .Which.Message.Should().Contain("firstQuestionRow");
    }

    [Fact]
    public void Parse_SectionRow_FirstLessThanSectionRow_ThrowsFormatException()
    {
        var act = () => LayoutParser.Parse([], ["5:4-10"], "C", "C", "C");

        act.Should().Throw<FormatException>()
            .Which.Message.Should().Contain("firstQuestionRow");
    }

    [Fact]
    public void Parse_SectionRow_LastLessThanFirst_ThrowsFormatException()
    {
        var act = () => LayoutParser.Parse([], ["5:6-5"], "C", "C", "C");

        act.Should().Throw<FormatException>()
            .Which.Message.Should().Contain("lastQuestionRow");
    }

    [Fact]
    public void Parse_SectionRow_NonIntegerSectionPart_ThrowsFormatException()
    {
        var act = () => LayoutParser.Parse([], ["tundra:6-10"], "C", "C", "C");

        act.Should().Throw<FormatException>()
            .Which.Message.Should().Contain("sectionRow");
    }

    [Fact]
    public void Parse_SectionRow_GarbageEntry_ThrowsFormatException()
    {
        var act = () => LayoutParser.Parse([], ["???"], "C", "C", "C");

        act.Should().Throw<FormatException>();
    }
}
