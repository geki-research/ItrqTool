using FluentAssertions;
using Xunit;
using ItrqTool.Tasks.GeneralDataDiff;

namespace ItrqTool.Tasks.Tests;

public sealed class GeneralDataConfigTests
{
    // ── Success cases ──────────────────────────────────────────────────────────

    [Fact]
    public void ParsedSections_SingleSectionSingleQuestion_ReturnsExpected()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1)"] };

        var sections = cfg.ParsedSections;

        sections.Should().HaveCount(1);
        sections[0].SectionRow.Should().Be(13);
        sections[0].Questions.Should().HaveCount(1);
        sections[0].Questions[0].Should().Be(new QuestionDefinition(14, 1));
    }

    [Fact]
    public void ParsedSections_SingleSectionMultipleQuestions_ReturnsExpected()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(1), 15(1), 16(2)"] };

        var sections = cfg.ParsedSections;

        sections.Should().HaveCount(1);
        sections[0].Questions.Should().HaveCount(3);
        sections[0].Questions[0].Should().Be(new QuestionDefinition(14, 1));
        sections[0].Questions[1].Should().Be(new QuestionDefinition(15, 1));
        sections[0].Questions[2].Should().Be(new QuestionDefinition(16, 2));
    }

    [Fact]
    public void ParsedSections_SectionWithSparseRows_ReturnsExpected()
    {
        var cfg = new GeneralDataConfig
        {
            SectionRows = ["13:14(1), 15(1), 16(1), 17(1), 18(3), 21(2)"]
        };

        var sections = cfg.ParsedSections;

        sections.Should().HaveCount(1);
        sections[0].Questions.Should().HaveCount(6);
        sections[0].Questions[5].Should().Be(new QuestionDefinition(21, 2));
    }

    [Fact]
    public void ParsedSections_MultipleSections_ReturnsAllInOrder()
    {
        var cfg = new GeneralDataConfig
        {
            SectionRows = ["13:14(1)", "21:22(4), 26(1)"]
        };

        var sections = cfg.ParsedSections;

        sections.Should().HaveCount(2);
        sections[0].SectionRow.Should().Be(13);
        sections[0].Questions.Should().HaveCount(1);
        sections[1].SectionRow.Should().Be(21);
        sections[1].Questions.Should().HaveCount(2);
    }

    [Fact]
    public void ParsedSections_WhitespaceInEntry_ParsesCorrectly()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["  13 : 14 ( 1 ), 15(1) "] };

        var sections = cfg.ParsedSections;

        sections.Should().HaveCount(1);
        sections[0].SectionRow.Should().Be(13);
        sections[0].Questions.Should().HaveCount(2);
        sections[0].Questions[0].Should().Be(new QuestionDefinition(14, 1));
        sections[0].Questions[1].Should().Be(new QuestionDefinition(15, 1));
    }

    // ── Per-entry failure cases ────────────────────────────────────────────────

    [Fact]
    public void ParsedSections_NoColon_Throws()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["13 14(1)"] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParsedSections_NoQuestions_Throws()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["13:"] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParsedSections_MissingRowspanParen_Throws()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["13:14"] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParsedSections_ZeroRowspan_Throws()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(0)"] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParsedSections_NegativeRowspan_Throws()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(-1)"] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParsedSections_NonIntegerSectionRow_Throws()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["abc:14(1)"] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>().WithMessage("*sectionRow*");
    }

    [Fact]
    public void ParsedSections_NonIntegerStartRow_Throws()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["13:abc(1)"] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParsedSections_QuestionRowEqualsSectionRow_Throws()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["13:13(1)"] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParsedSections_QuestionRowBelowSectionRow_Throws()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["13:10(1)"] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParsedSections_OverlappingQuestionsWithinSection_Throws()
    {
        // Q1 spans 14-16, Q2 starts at 15 → overlap
        var cfg = new GeneralDataConfig { SectionRows = ["13:14(3), 15(1)"] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParsedSections_QuestionsOutOfOrderWithinSection_Throws()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["13:20(1), 14(1)"] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParsedSections_NegativeSectionRow_Throws()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["-5:14(1)"] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>().WithMessage("*sectionRow*");
    }

    [Fact]
    public void ParsedSections_ZeroSectionRow_Throws()
    {
        var cfg = new GeneralDataConfig { SectionRows = ["0:14(1)"] };
        var act = () => cfg.ParsedSections;
        act.Should().Throw<FormatException>().WithMessage("*sectionRow*");
    }

    // ── Default values ─────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_AreAsExpected()
    {
        var cfg = new GeneralDataConfig();

        cfg.SheetName.Should().Be("General Data");
        cfg.NumberColumn.Should().Be("B");
        cfg.TextColumn.Should().Be("C");
        cfg.AnswerColumns.Should().Equal("D", "E", "F");
        cfg.ExplanationColumn.Should().Be("G");
        cfg.SectionRows.Should().BeEmpty();
    }
}
