using FluentAssertions;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Parsing;

public sealed class QuestionNumberParserTests
{
    // ── ExtractNumber ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.1) How is governance documented?", "1.1")]
    [InlineData("2.3 Describe the escalation route", "2.3")]
    [InlineData("  4.7)   leading and trailing space ", "4.7")]
    [InlineData("Explain the retention schedule", null)]   // no numeric prefix
    [InlineData("12 Approval workflow without dot", null)] // single integer, not "n.m"
    public void ExtractNumber_ReturnsPrefixOrNull(string text, string? expected)
    {
        QuestionNumberParser.ExtractNumber(text).Should().Be(expected);
    }

    [Fact]
    public void ExtractNumber_DeeperPrefix_CapturesOnlyFirstTwoSegments()
    {
        // PrefixPattern matches only "<int>.<int>" — a deeper "1.1.1" yields "1.1".
        QuestionNumberParser.ExtractNumber("1.1.1) sub-question on access reviews")
            .Should().Be("1.1");
    }

    // ── StripPrefix ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.1) How is governance documented?", "How is governance documented?")]
    [InlineData("2.3 Describe the escalation route", "Describe the escalation route")]
    [InlineData("Explain the retention schedule", "Explain the retention schedule")]
    public void StripPrefix_RemovesNumericPrefixWhenPresent(string text, string expected)
    {
        QuestionNumberParser.StripPrefix(text).Should().Be(expected);
    }

    // ── HasUnsupportedDeeperPrefix ───────────────────────────────────────────────

    [Theory]
    [InlineData("1.1.1 access review cadence", true)]
    [InlineData("3.2.4.5 deeply nested item", true)]
    [InlineData("1.1) supported two-segment", false)]
    [InlineData("Narrative with no number", false)]
    public void HasUnsupportedDeeperPrefix_DetectsThreePlusSegments(string text, bool expected)
    {
        QuestionNumberParser.HasUnsupportedDeeperPrefix(text).Should().Be(expected);
    }
}
