using FluentAssertions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Tasks.CellRangeDiff;

namespace ItrqTool.Tasks.Tests;

public sealed class CellRangeDiffEngineTests
{
    private static ExcelCellStructure Cell(string? text = null,
        string? dvType = null, string? dvOp = null, string? dvFormula = null, string? dvFormula2 = null,
        string? cfType = null, string? cfOp = null, string? cfValue = null, string? cfValue2 = null)
        => new(text, dvType, dvFormula, cfOp, dvOp, dvFormula2, cfType, cfValue, cfValue2);

    private static IReadOnlyDictionary<string, ExcelCellStructure> D(params (string, ExcelCellStructure)[] pairs)
        => pairs.ToDictionary(p => p.Item1, p => p.Item2, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Diff_ValueChange_CellIsChanged()
    {
        var f1 = D(("A1", Cell("old")));
        var f2 = D(("A1", Cell("new")));

        var result = CellRangeDiffEngine.Diff(f1, f2, ["A1"], CompareScope.Value);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].Address.Should().Be("A1");
        result.Changed[0].TextChanged.Should().BeTrue();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_DvChange_ValueScope_CellIsUnchanged()
    {
        // With Value scope, DV differences are ignored
        var f1 = D(("B2", Cell(dvType: "List", dvFormula: "\"Yes,No\"")));
        var f2 = D(("B2", Cell(dvType: "List", dvFormula: "\"Yes,No,N/A\"")));

        var result = CellRangeDiffEngine.Diff(f1, f2, ["B2"], CompareScope.Value);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_DvChange_ValueAndDvCfScope_CellIsChanged()
    {
        var f1 = D(("B2", Cell(dvType: "List", dvFormula: "\"Yes,No\"")));
        var f2 = D(("B2", Cell(dvType: "List", dvFormula: "\"Yes,No,N/A\"")));

        var result = CellRangeDiffEngine.Diff(f1, f2, ["B2"], CompareScope.ValueAndDvCf);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].DvChanged.Should().BeTrue();
        result.Changed[0].TextChanged.Should().BeFalse();
    }

    [Fact]
    public void Diff_CfChange_ValueAndDvCfScope_CellIsChanged()
    {
        var f1 = D(("C3", Cell(cfType: "CellIs", cfOp: "GreaterThan", cfValue: "5")));
        var f2 = D(("C3", Cell(cfType: "CellIs", cfOp: "GreaterThan", cfValue: "10")));

        var result = CellRangeDiffEngine.Diff(f1, f2, ["C3"], CompareScope.ValueAndDvCf);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].CfChanged.Should().BeTrue();
        result.Changed[0].TextChanged.Should().BeFalse();
    }

    [Fact]
    public void Diff_NoChange_CellIsUnchanged()
    {
        var f1 = D(("A1", Cell("same")));
        var f2 = D(("A1", Cell("same")));

        var result = CellRangeDiffEngine.Diff(f1, f2, ["A1"], CompareScope.ValueAndDvCf);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_WhitespaceOnlyDifference_CellIsUnchanged()
    {
        // Leading/trailing whitespace is not a meaningful change
        var f1 = D(("D4", Cell("  hello  ")));
        var f2 = D(("D4", Cell("hello")));

        var result = CellRangeDiffEngine.Diff(f1, f2, ["D4"], CompareScope.Value);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_AddressAbsentInFile1_TreatedAsEmpty()
    {
        var f1 = D();
        var f2 = D(("E5", Cell("only in f2")));

        var result = CellRangeDiffEngine.Diff(f1, f2, ["E5"], CompareScope.Value);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].Cell1.TextValue.Should().BeNull();
        result.Changed[0].TextChanged.Should().BeTrue();
    }
}
