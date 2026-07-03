using System.Globalization;
using FluentAssertions;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Checks;

// Breadth proof for the pure typed-dispatch evaluator: one Conformant + one NotConformant per DV
// type (incl. type-violation → NotConformant for non-parseable values), Between/NotBetween using
// Formula2, List membership (resolved) + unresolved → UnresolvableList, Custom → NotCheckable,
// AnyValue/null → Conformant, missing bound/operator → NotCheckable, unknown type → NotCheckable.
public sealed class DvConformanceEvaluatorTests
{
    private static DvConformanceResult Eval(
        string value, string? type, string? op = null, string? f1 = null, string? f2 = null,
        IReadOnlyList<string>? list = null, object? sourceNative = null)
        => DvConformanceEvaluator.Evaluate(value, type, op, f1, f2, list, sourceNative);

    private static string Serial(int y, int m, int d) =>
        new DateTime(y, m, d).ToOADate().ToString(CultureInfo.InvariantCulture);

    // ── AnyValue / null / empty type → Conformant (no constraint) ──
    [Theory]
    [InlineData("anything", "AnyValue")]
    [InlineData("anything", null)]
    [InlineData("anything", "")]
    public void NoConstraint_Conformant(string value, string? type)
        => Eval(value, type).Should().Be(DvConformanceResult.Conformant);

    // ── WholeNumber ──
    [Theory]
    [InlineData("5",   "EqualOrGreaterThan", "0", DvConformanceResult.Conformant)]
    [InlineData("0",   "EqualOrGreaterThan", "0", DvConformanceResult.Conformant)]
    [InlineData("-1",  "EqualOrGreaterThan", "0", DvConformanceResult.NotConformant)]
    [InlineData("3.5", "EqualOrGreaterThan", "0", DvConformanceResult.NotConformant)]   // non-integer
    [InlineData("abc", "EqualOrGreaterThan", "0", DvConformanceResult.NotConformant)]   // non-numeric
    public void WholeNumber(string value, string op, string f1, DvConformanceResult expected)
        => Eval(value, "WholeNumber", op, f1).Should().Be(expected);

    // ── Decimal ──
    [Theory]
    [InlineData("3.5", "GreaterThan", "3", DvConformanceResult.Conformant)]
    [InlineData("2.5", "GreaterThan", "3", DvConformanceResult.NotConformant)]
    [InlineData("x",   "GreaterThan", "3", DvConformanceResult.NotConformant)]          // non-numeric
    public void DecimalType(string value, string op, string f1, DvConformanceResult expected)
        => Eval(value, "Decimal", op, f1).Should().Be(expected);

    // ── TextLength ──
    [Theory]
    [InlineData("abc",    "LessThan", "5", DvConformanceResult.Conformant)]    // len 3 < 5
    [InlineData("abcdef", "LessThan", "5", DvConformanceResult.NotConformant)] // len 6
    [InlineData("abc",    "EqualTo",  "3", DvConformanceResult.Conformant)]
    public void TextLength(string value, string op, string f1, DvConformanceResult expected)
        => Eval(value, "TextLength", op, f1).Should().Be(expected);

    // ── Between / NotBetween (uses Formula2) ──
    [Theory]
    [InlineData("5",  "Between",    "1", "10", DvConformanceResult.Conformant)]
    [InlineData("11", "Between",    "1", "10", DvConformanceResult.NotConformant)]
    [InlineData("11", "NotBetween", "1", "10", DvConformanceResult.Conformant)]
    [InlineData("5",  "NotBetween", "1", "10", DvConformanceResult.NotConformant)]
    public void BetweenOperators(string value, string op, string f1, string f2, DvConformanceResult expected)
        => Eval(value, "WholeNumber", op, f1, f2).Should().Be(expected);

    // ── Date (value as OADate serial OR formatted; bound is a serial) ──
    [Fact]
    public void Date_SerialValue_WithinBound_Conformant()
        => Eval(Serial(2024, 1, 15), "Date", "EqualOrGreaterThan", Serial(2024, 1, 1))
            .Should().Be(DvConformanceResult.Conformant);

    [Fact]
    public void Date_FormattedValue_Parsed_Conformant()
        => Eval("2024-01-15", "Date", "EqualOrGreaterThan", Serial(2024, 1, 1))
            .Should().Be(DvConformanceResult.Conformant);

    [Fact]
    public void Date_BeforeBound_NotConformant()
        => Eval(Serial(2023, 12, 31), "Date", "EqualOrGreaterThan", Serial(2024, 1, 1))
            .Should().Be(DvConformanceResult.NotConformant);

    [Fact]
    public void Date_NonDateValue_NotConformant()
        => Eval("not-a-date", "Date", "EqualOrGreaterThan", Serial(2024, 1, 1))
            .Should().Be(DvConformanceResult.NotConformant);

    // ── List ──
    [Fact]
    public void List_Member_Conformant()
        => Eval("Yes", "List", list: new[] { "Yes", "No" }).Should().Be(DvConformanceResult.Conformant);

    [Fact]
    public void List_NonMember_NotConformant()
        => Eval("Maybe", "List", list: new[] { "Yes", "No" }).Should().Be(DvConformanceResult.NotConformant);

    [Fact]
    public void List_UnresolvedNull_UnresolvableList()
        => Eval("Yes", "List", list: null).Should().Be(DvConformanceResult.UnresolvableList);

    // ── Custom → NotCheckable (no formula engine) ──
    [Fact]
    public void Custom_NotCheckable()
        => Eval("anything", "Custom", f1: "ISNUMBER(A1)").Should().Be(DvConformanceResult.NotCheckable);

    // ── Missing bound / operator → NotCheckable (cannot judge; never false NotConformant) ──
    [Fact]
    public void MissingBound_NotCheckable()
        => Eval("5", "WholeNumber", "EqualOrGreaterThan", null).Should().Be(DvConformanceResult.NotCheckable);

    [Fact]
    public void MissingOperator_NotCheckable()
        => Eval("5", "WholeNumber", null, "0").Should().Be(DvConformanceResult.NotCheckable);

    [Fact]
    public void MissingSecondBound_Between_NotCheckable()
        => Eval("5", "WholeNumber", "Between", "1", null).Should().Be(DvConformanceResult.NotCheckable);

    // ── Unknown type → NotCheckable (conservative) ──
    [Fact]
    public void UnknownType_NotCheckable()
        => Eval("5", "Bogus", "EqualTo", "5").Should().Be(DvConformanceResult.NotCheckable);

    // ── sourceNative (BL-058): native-numeric compare bypasses the invariant text parse,
    // fixing the comma-decimal false-reject; sourceNative: null keeps the text path unchanged.
    [Fact]
    public void Decimal_CommaText_WithNative_Conformant()
        => Eval("9,1", "Decimal", "Between", "0", "100", sourceNative: 9.1d)
            .Should().Be(DvConformanceResult.Conformant);

    [Fact]
    public void Decimal_CommaText_WithoutNative_NotConformant()
        => Eval("9,1", "Decimal", "Between", "0", "100", sourceNative: null)
            .Should().Be(DvConformanceResult.NotConformant);

    [Fact]
    public void WholeNumber_NonIntegralNative_NotConformant()
        => Eval("3,5", "WholeNumber", "EqualOrGreaterThan", "0", sourceNative: 3.5d)
            .Should().Be(DvConformanceResult.NotConformant);

    [Fact]
    public void WholeNumber_IntegralNative_OperatorApplied()
        => Eval("3,0", "WholeNumber", "EqualOrGreaterThan", "0", sourceNative: 3.0d)
            .Should().Be(DvConformanceResult.Conformant);
}
