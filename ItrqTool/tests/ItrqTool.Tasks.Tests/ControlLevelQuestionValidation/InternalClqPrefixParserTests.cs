using FluentAssertions;
using ItrqTool.Tasks.ControlLevelQuestionValidation;
using Xunit;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidation;

public sealed class InternalClqPrefixParserTests
{
    // ── ExtractNumber ─────────────────────────────────────────────────────────

    [Fact]
    public void ExtractNumber_WithParen_ReturnsNumber()
    {
        InternalClqPrefixParser.ExtractNumber("1.2) x").Should().Be("1.2");
        InternalClqPrefixParser.ExtractNumber("10.3) Multi-digit").Should().Be("10.3");
        InternalClqPrefixParser.ExtractNumber("1.1) What is risk?").Should().Be("1.1");
    }

    [Fact]
    public void ExtractNumber_WithoutParen_ReturnsNumber()
    {
        InternalClqPrefixParser.ExtractNumber("1.2 x").Should().Be("1.2");
        InternalClqPrefixParser.ExtractNumber("3.5  x").Should().Be("3.5");
    }

    [Fact]
    public void ExtractNumber_NoPrefix_ReturnsNull()
    {
        InternalClqPrefixParser.ExtractNumber("Some text").Should().BeNull();
        InternalClqPrefixParser.ExtractNumber("  No prefix here  ").Should().BeNull();
    }

    // ── StripPrefix ───────────────────────────────────────────────────────────

    [Fact]
    public void StripPrefix_WithParen_ReturnsText()
    {
        InternalClqPrefixParser.StripPrefix("1.2) x").Should().Be("x");
        InternalClqPrefixParser.StripPrefix("10.3) Multi-digit prefix").Should().Be("Multi-digit prefix");
    }

    [Fact]
    public void StripPrefix_WithoutParen_ReturnsText()
    {
        InternalClqPrefixParser.StripPrefix("1.2 x").Should().Be("x");
        InternalClqPrefixParser.StripPrefix("3.5  x").Should().Be("x");
    }

    [Fact]
    public void StripPrefix_NoPrefix_ReturnsTrimmedText()
    {
        InternalClqPrefixParser.StripPrefix("  No prefix here  ").Should().Be("No prefix here");
        InternalClqPrefixParser.StripPrefix("What is risk?").Should().Be("What is risk?");
    }

    // ── HasUnsupportedDeeperPrefix ────────────────────────────────────────────

    [Fact]
    public void HasUnsupportedDeeperPrefix_ThreeLevel_ReturnsTrue()
    {
        InternalClqPrefixParser.HasUnsupportedDeeperPrefix("10.4.2) x").Should().BeTrue();
        InternalClqPrefixParser.HasUnsupportedDeeperPrefix("1.2.3 x").Should().BeTrue();
    }

    [Fact]
    public void HasUnsupportedDeeperPrefix_TwoLevel_ReturnsFalse()
    {
        InternalClqPrefixParser.HasUnsupportedDeeperPrefix("1.2) x").Should().BeFalse();
        InternalClqPrefixParser.HasUnsupportedDeeperPrefix("10.3 x").Should().BeFalse();
    }

    [Fact]
    public void HasUnsupportedDeeperPrefix_NoPrefix_ReturnsFalse()
    {
        InternalClqPrefixParser.HasUnsupportedDeeperPrefix("No prefix text").Should().BeFalse();
        InternalClqPrefixParser.HasUnsupportedDeeperPrefix("  leading spaces  ").Should().BeFalse();
    }

    [Fact]
    public void ThreeLevelInput_YieldsTruncatedNumber_AndSetsFlag()
    {
        // The 2-level regex matches "10.4" from "10.4.2) text"; ".2) text" remains as QuestionText.
        // HasUnsupportedDeeperPrefix detects the 3-level form independently.
        InternalClqPrefixParser.ExtractNumber("10.4.2) text").Should().Be("10.4");
        InternalClqPrefixParser.StripPrefix("10.4.2) text").Should().Be(".2) text");
        InternalClqPrefixParser.HasUnsupportedDeeperPrefix("10.4.2) text").Should().BeTrue();
    }
}
