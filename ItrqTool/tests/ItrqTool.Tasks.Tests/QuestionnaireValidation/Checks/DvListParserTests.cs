using FluentAssertions;
using ItrqTool.Tasks.Shared;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Checks;

// Coverage for DvListParser: inline parse (quotes / spaces / empties / single) and the PRECISE
// source classifier — critically, that a bare named range is NamedRange, NOT inline (the bug the
// loose '$'-only heuristic would produce).
public sealed class DvListParserTests
{
    [Theory]
    [InlineData("\"Yes,No\"",   new[] { "Yes", "No" })]
    [InlineData("Yes,No",       new[] { "Yes", "No" })]
    [InlineData("\"1,2,3,4\"",  new[] { "1", "2", "3", "4" })]
    [InlineData(" Yes , No ",   new[] { "Yes", "No" })]
    [InlineData("Yes,,No",      new[] { "Yes", "No" })]    // empties dropped
    [InlineData("\"Single\"",   new[] { "Single" })]
    public void ParseInline_SplitsTrimsDropsEmpties(string formula, string[] expected)
        => DvListParser.ParseInline(formula).Should().Equal(expected);

    [Fact]
    public void ParseInline_Empty_ReturnsEmpty()
        => DvListParser.ParseInline("").Should().BeEmpty();

    [Theory]
    [InlineData("\"Yes,No\"",        DvListSourceKind.Inline)]
    [InlineData("Yes,No",            DvListSourceKind.Inline)]
    [InlineData("\"Single\"",        DvListSourceKind.Inline)]
    [InlineData("Sheet1!$A$1:$A$3",  DvListSourceKind.RangeRef)]
    [InlineData("$D$1:$D$3",         DvListSourceKind.RangeRef)]
    [InlineData("D1:D3",             DvListSourceKind.RangeRef)]
    [InlineData("A1",                DvListSourceKind.RangeRef)]
    [InlineData("=Sheet1!$A$1:$A$3", DvListSourceKind.RangeRef)]
    [InlineData("MyList",            DvListSourceKind.NamedRange)]
    [InlineData("=MyList",           DvListSourceKind.NamedRange)]
    public void ClassifySource_PreciseKind(string formula, DvListSourceKind expected)
        => DvListParser.ClassifySource(formula).Should().Be(expected);

    [Fact]
    public void ClassifySource_BareName_IsNamedRange_NotInline()
        => DvListParser.ClassifySource("MyList").Should().Be(DvListSourceKind.NamedRange);
}
