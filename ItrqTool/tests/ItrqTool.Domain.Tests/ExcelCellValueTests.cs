using FluentAssertions;
using Xunit;
using ItrqTool.Domain;

namespace ItrqTool.Domain.Tests;

public sealed class ExcelCellValueTests
{
    [Fact]
    public void As_ReturnsValue_WhenTypeMatches()
    {
        var cell = new ExcelCellValue(42.0, typeof(double));
        cell.As<double>().Should().Be(42.0);
    }

    [Fact]
    public void As_ReturnsDefault_WhenTypeDoesNotMatch()
    {
        var cell = new ExcelCellValue("hello", typeof(string));
        cell.As<double>().Should().Be(default);
    }

    [Fact]
    public void As_ReturnsNull_WhenValueIsNull()
    {
        var cell = new ExcelCellValue(null, null);
        cell.As<string>().Should().BeNull();
    }

    [Fact]
    public void As_ReturnsString_WhenValueIsString()
    {
        var cell = new ExcelCellValue("test", typeof(string));
        cell.As<string>().Should().Be("test");
    }

    [Fact]
    public void As_ReturnsDateTime_WhenValueIsDateTime()
    {
        var dt = new DateTime(2024, 1, 15);
        var cell = new ExcelCellValue(dt, typeof(DateTime));
        cell.As<DateTime>().Should().Be(dt);
    }
}
