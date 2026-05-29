using FluentAssertions;
using Xunit;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.Tests;

public sealed class CfDisplayFormatterTests
{
    [Fact]
    public void Format_NullType_ReturnsDash()
    {
        CfDisplayFormatter.Format(null, null, null, null).Should().Be("—");
    }

    [Fact]
    public void Format_CellIs_GreaterThan_5()
    {
        CfDisplayFormatter.Format("CellIs", "GreaterThan", "5", null)
            .Should().Be("greater than 5");
    }

    [Fact]
    public void Format_CellIs_Between_1And10()
    {
        CfDisplayFormatter.Format("CellIs", "Between", "1", "10")
            .Should().Be("between 1 and 10");
    }

    [Fact]
    public void Format_CellIs_Equal_X()
    {
        CfDisplayFormatter.Format("CellIs", "Equal", "X", null)
            .Should().Be("equal to X");
    }

    [Fact]
    public void Format_Expression_ReturnsFormula()
    {
        CfDisplayFormatter.Format("Expression", null, "A1>5", null)
            .Should().Be("Formula: A1>5");
    }

    [Fact]
    public void Format_FallbackType_ReturnsTypeName()
    {
        CfDisplayFormatter.Format("ColorScale", null, null, null)
            .Should().Be("ColorScale");
    }
}
