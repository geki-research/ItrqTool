using FluentAssertions;
using Xunit;
using ItrqTool.Tasks.GeneralDataDiff;

namespace ItrqTool.Tasks.Tests;

public sealed class GeneralDataTextSimilarityTests
{
    [Fact]
    public void Score_IdenticalStrings_ReturnsOne()
        => TextSimilarity.Score("hello world", "hello world").Should().Be(1.0);

    [Fact]
    public void Score_BothEmpty_ReturnsOne()
        => TextSimilarity.Score("", "").Should().Be(1.0);

    [Fact]
    public void Score_OneEmpty_ReturnsZero()
    {
        TextSimilarity.Score("hello", "").Should().Be(0.0);
        TextSimilarity.Score("", "world").Should().Be(0.0);
    }

    [Fact]
    public void Score_CompletelyDifferentStrings_IsLessThanHalf()
        => TextSimilarity.Score("aaa", "zzz").Should().BeLessThan(0.5);

    [Fact]
    public void Score_NormalizesExtraWhitespace()
        => TextSimilarity.Score("foo bar", "foo  bar").Should().Be(1.0);

    [Fact]
    public void Score_NormalizesCase()
        => TextSimilarity.Score("Hello World", "hello world").Should().Be(1.0);

    [Fact]
    public void Score_SlightlyDifferentStrings_IsHighScore()
        => TextSimilarity.Score("What is the risk?", "What is a risk?").Should().BeGreaterThan(0.5);
}
