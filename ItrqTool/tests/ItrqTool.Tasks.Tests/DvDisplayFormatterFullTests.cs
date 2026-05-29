using System.Globalization;
using FluentAssertions;
using Xunit;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.Tests;

public sealed class DvDisplayFormatterFullTests
{
    [Fact]
    public void FormatFull_NullType_ReturnsDash()
    {
        DvDisplayFormatter.FormatFull(null, null, null, null).Should().Be("—");
    }

    [Fact]
    public void FormatFull_AnyValue_ReturnsDash()
    {
        DvDisplayFormatter.FormatFull("AnyValue", null, null, null).Should().Be("—");
    }

    [Fact]
    public void FormatFull_ListType_InlineList_ReturnsFormattedList()
    {
        DvDisplayFormatter.FormatFull("List", null, "\"Yes,No,N/A\"", null)
            .Should().Be("List: Yes | No | N/A");
    }

    [Fact]
    public void FormatFull_CustomType_ReturnsCustomFormula()
    {
        DvDisplayFormatter.FormatFull("Custom", null, "=A1>0", null)
            .Should().Be("Custom: =A1>0");
    }

    [Fact]
    public void FormatFull_Decimal_GreaterThanOrEqualTo_0()
    {
        DvDisplayFormatter.FormatFull("Decimal", "EqualOrGreaterThan", "0", null)
            .Should().Be("Decimal, greater than or equal to, 0");
    }

    [Fact]
    public void FormatFull_WholeNumber_Between_1And10()
    {
        DvDisplayFormatter.FormatFull("WholeNumber", "Between", "1", "10")
            .Should().Be("Whole number, between, 1 and 10");
    }

    [Fact]
    public void FormatFull_TextLength_LessThan_50()
    {
        DvDisplayFormatter.FormatFull("TextLength", "LessThan", "50", null)
            .Should().Be("Text length, less than, 50");
    }

    [Fact]
    public void FormatFull_Date_GreaterThan_ReturnsIsoDate()
    {
        var serial = new DateTime(2024, 1, 1).ToOADate().ToString(CultureInfo.InvariantCulture);
        DvDisplayFormatter.FormatFull("Date", "GreaterThan", serial, null)
            .Should().Be("Date, greater than, 2024-01-01");
    }

    [Fact]
    public void FormatFull_Date_Between_ReturnsIsoDateRange()
    {
        var s1 = new DateTime(2024, 1, 1).ToOADate().ToString(CultureInfo.InvariantCulture);
        var s2 = new DateTime(2024, 12, 31).ToOADate().ToString(CultureInfo.InvariantCulture);
        DvDisplayFormatter.FormatFull("Date", "Between", s1, s2)
            .Should().Be("Date, between, 2024-01-01 and 2024-12-31");
    }

    [Fact]
    public void FormatFull_WholeNumber_NotBetween_1And10()
    {
        DvDisplayFormatter.FormatFull("WholeNumber", "NotBetween", "1", "10")
            .Should().Be("Whole number, not between, 1 and 10");
    }

    [Fact]
    public void FormatFull_Time_ValidSerial_ReturnsTimeFormat()
    {
        // 0.5 OADate serial = 12:00:00
        var serial = (0.5).ToString(CultureInfo.InvariantCulture);
        DvDisplayFormatter.FormatFull("Time", "GreaterThan", serial, null)
            .Should().Be("Time, greater than, 12:00:00");
    }

    [Fact]
    public void FormatFull_Date_WithTimeOfDay_IncludesTimeComponent()
    {
        var dt = new DateTime(2024, 6, 15, 9, 30, 0);
        var serial = dt.ToOADate().ToString(CultureInfo.InvariantCulture);
        DvDisplayFormatter.FormatFull("Date", "EqualTo", serial, null)
            .Should().Be("Date, equal to, 2024-06-15 09:30:00");
    }

    [Fact]
    public void FormatFull_Date_UnparseableFormula_PassesThrough()
    {
        DvDisplayFormatter.FormatFull("Date", "GreaterThan", "not-a-number", null)
            .Should().Be("Date, greater than, not-a-number");
    }
}
