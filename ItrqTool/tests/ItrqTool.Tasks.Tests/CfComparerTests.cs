using FluentAssertions;
using Xunit;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.Tests;

public sealed class CfComparerTests
{
    [Fact]
    public void IsCfChanged_Identical_ReturnsFalse()
    {
        CfComparer.IsCfChanged(
            "CellIs", "GreaterThan", "5", null,
            "CellIs", "GreaterThan", "5", null)
            .Should().BeFalse();
    }

    [Fact]
    public void IsCfChanged_TypeDiffers_ReturnsTrue()
    {
        CfComparer.IsCfChanged(
            "CellIs",     "GreaterThan", "5", null,
            "Expression", "GreaterThan", "5", null)
            .Should().BeTrue();
    }

    [Fact]
    public void IsCfChanged_OperatorDiffers_ReturnsTrue()
    {
        CfComparer.IsCfChanged(
            "CellIs", "GreaterThan", "5", null,
            "CellIs", "LessThan",    "5", null)
            .Should().BeTrue();
    }

    [Fact]
    public void IsCfChanged_ValueDiffers_ReturnsTrue()
    {
        CfComparer.IsCfChanged(
            "CellIs", "GreaterThan", "5",  null,
            "CellIs", "GreaterThan", "10", null)
            .Should().BeTrue();
    }

    [Fact]
    public void IsCfChanged_Value2Differs_ReturnsTrue()
    {
        CfComparer.IsCfChanged(
            "CellIs", "Between", "1", "10",
            "CellIs", "Between", "1", "20")
            .Should().BeTrue();
    }
}
