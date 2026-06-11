using FluentAssertions;
using ItrqTool.Tasks.ControlLevelQuestionValidation;
using Xunit;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidation;

// Smoke tests that lock the per-sibling COPIES of TextSimilarity and
// HungarianAlgorithm in the ControlLevelQuestionValidation namespace (duplicated
// from ControlLevelQuestionDiff per the duplicate-and-defer convention).
public sealed class ClqAlignmentPrimitivesTests
{
    [Fact]
    public void TextSimilarity_BothEmpty_IsOne()
        => TextSimilarity.Score("", "").Should().Be(1.0);

    [Fact]
    public void TextSimilarity_OneEmpty_IsZero()
        => TextSimilarity.Score("hello", "").Should().Be(0.0);

    [Fact]
    public void TextSimilarity_Identical_IsOne()
        => TextSimilarity.Score("What is your risk appetite?", "What is your risk appetite?").Should().Be(1.0);

    [Fact]
    public void Hungarian_2x2_PairsOptimally_Identity()
    {
        var profit = new double[,] { { 0.9, 0.1 }, { 0.1, 0.9 } };

        var assignment = HungarianAlgorithm.SolveMaximumAssignment(profit);

        assignment.Should().Equal(0, 1);
    }

    [Fact]
    public void Hungarian_2x2_PairsOptimally_Swapped()
    {
        var profit = new double[,] { { 0.1, 0.9 }, { 0.9, 0.1 } };

        var assignment = HungarianAlgorithm.SolveMaximumAssignment(profit);

        assignment.Should().Equal(1, 0);
    }
}
