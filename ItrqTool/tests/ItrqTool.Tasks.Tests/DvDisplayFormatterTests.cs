using FluentAssertions;
using Xunit;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.Tests;

public sealed class DvDisplayFormatterTests
{
    [Fact]
    public void FormatDv_NullType_ReturnsDash()
    {
        DvDisplayFormatter.FormatDv(null, null).Should().Be("—");
    }

    [Fact]
    public void FormatDv_NonListType_ReturnsTypeName()
    {
        DvDisplayFormatter.FormatDv("WholeNumber", null).Should().Be("WholeNumber");
    }

    [Fact]
    public void FormatDv_ListTypeInlineFormula_ReturnsFormattedItems()
    {
        DvDisplayFormatter.FormatDv("List", "\"Yes,No,N/A\"")
            .Should().Be("List: Yes | No | N/A");
    }

    [Fact]
    public void FormatDv_ListTypeRangeFormula_ReturnsListPlusFormula()
    {
        DvDisplayFormatter.FormatDv("List", "$Sheet.$A$1:$A$5")
            .Should().Be("List: $Sheet.$A$1:$A$5");
    }

    [Fact]
    public void FormatDv_ListTypeNullFormula_ReturnsList()
    {
        DvDisplayFormatter.FormatDv("List", null).Should().Be("List");
    }
}
