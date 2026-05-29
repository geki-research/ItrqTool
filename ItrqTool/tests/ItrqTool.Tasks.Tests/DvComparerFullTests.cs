using FluentAssertions;
using Xunit;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.Tests;

public sealed class DvComparerFullTests
{
    [Fact]
    public void IsDvChangedFull_OperatorOnlyChange_ReturnsTrue()
    {
        DvComparer.IsDvChangedFull(
            "Decimal", "GreaterThan", "0", null,
            "Decimal", "LessThan",    "0", null)
            .Should().BeTrue();
    }

    [Fact]
    public void IsDvChangedFull_SecondValueOnlyChange_ReturnsTrue()
    {
        DvComparer.IsDvChangedFull(
            "WholeNumber", "Between", "0", "100",
            "WholeNumber", "Between", "0", "200")
            .Should().BeTrue();
    }

    [Fact]
    public void IsDvChangedFull_FirstValueOnlyChange_ReturnsTrue()
    {
        DvComparer.IsDvChangedFull(
            "Decimal", "GreaterThan", "0", null,
            "Decimal", "GreaterThan", "1", null)
            .Should().BeTrue();
    }

    [Fact]
    public void IsDvChangedFull_TypeChange_ReturnsTrue()
    {
        DvComparer.IsDvChangedFull(
            "Decimal",     "GreaterThan", "0", null,
            "WholeNumber", "GreaterThan", "0", null)
            .Should().BeTrue();
    }

    [Fact]
    public void IsDvChangedFull_Identical_ReturnsFalse()
    {
        DvComparer.IsDvChangedFull(
            "Decimal", "GreaterThan", "0", null,
            "Decimal", "GreaterThan", "0", null)
            .Should().BeFalse();
    }

    [Fact]
    public void IsDvChangedFull_List_DifferentMembers_ReturnsTrue()
    {
        DvComparer.IsDvChangedFull(
            "List", null, "\"Yes,No\"", null,
            "List", null, "\"Yes,No,N/A\"", null)
            .Should().BeTrue();
    }

    [Fact]
    public void IsDvChangedFull_List_ReorderedSameMembers_ReturnsFalse()
    {
        DvComparer.IsDvChangedFull(
            "List", null, "\"Yes,No,N/A\"", null,
            "List", null, "\"N/A,Yes,No\"", null)
            .Should().BeFalse();
    }
}
