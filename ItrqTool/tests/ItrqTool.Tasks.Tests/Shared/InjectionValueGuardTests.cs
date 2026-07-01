using FluentAssertions;
using ItrqTool.Tasks.Shared;
using Xunit;

namespace ItrqTool.Tasks.Tests.Shared;

// Full Inject/Skip decision matrix for the pure IO-free InjectionValueGuard: the numeric<->date
// category pre-gate (both directions, and confirming it does not fire when sourceDvType is
// unknown), then every DvConformanceResult branch (Conformant/NotConformant/UnresolvableList/
// NotCheckable) including the NotCheckable sub-classification by recognised vs opaque target type.
public sealed class InjectionValueGuardTests
{
    private static InjectionCheckResult Eval(
        string sourceText, string? sourceDvType,
        string? targetDvType, string? targetDvOperator = null, string? targetDvFormula = null,
        string? targetDvFormula2 = null, IReadOnlyList<string>? targetResolvedListValues = null)
        => InjectionValueGuard.Evaluate(
            sourceText, sourceDvType, targetDvType, targetDvOperator, targetDvFormula, targetDvFormula2,
            targetResolvedListValues);

    // ── No constraint on the target ──
    [Fact]
    public void NoDvTarget_Inject()
        => Eval("anything", null, null).Decision.Should().Be(InjectionDecision.Inject);

    [Fact]
    public void AnyValueTarget_Inject()
        => Eval("anything", null, "AnyValue").Decision.Should().Be(InjectionDecision.Inject);

    // ── List target (Conformant / NotConformant / UnresolvableList) ──
    [Fact]
    public void List_MemberValue_Inject()
        => Eval("Yes", null, "List", targetResolvedListValues: new[] { "Yes", "No" })
            .Decision.Should().Be(InjectionDecision.Inject);

    [Fact]
    public void List_NonMemberValue_Skip_NotConformantReason()
    {
        var r = Eval("Maybe", null, "List", targetResolvedListValues: new[] { "Yes", "No" });
        r.Decision.Should().Be(InjectionDecision.Skip);
        r.SkipReason.Should().Contain("does not conform");
    }

    [Fact]
    public void List_UnresolvedNull_Skip_UnresolvableListReason()
    {
        var r = Eval("Yes", null, "List", targetResolvedListValues: null);
        r.Decision.Should().Be(InjectionDecision.Skip);
        r.SkipReason.Should().Be("target data-validation vocabulary could not be resolved");
    }

    // ── Numeric widening / narrowing (Conformant / NotConformant, no pre-gate — both Numeric) ──
    [Fact]
    public void WholeNumberToDecimal_Widen_Inject()
        => Eval("3", "WholeNumber", "Decimal", "EqualOrGreaterThan", "0")
            .Decision.Should().Be(InjectionDecision.Inject);

    [Fact]
    public void DecimalToWholeNumber_NonInteger_Skip()
    {
        var r = Eval("3.5", "Decimal", "WholeNumber", "EqualOrGreaterThan", "0");
        r.Decision.Should().Be(InjectionDecision.Skip);
        r.SkipReason.Should().Contain("does not conform");
    }

    [Fact]
    public void DecimalToWholeNumber_Bounded_Inject()
        => Eval("4", "Decimal", "WholeNumber", "EqualOrGreaterThan", "0")
            .Decision.Should().Be(InjectionDecision.Inject);

    [Fact]
    public void DecimalToWholeNumber_UnboundedNoOperator_Inject_NotCheckable2b()
        => Eval("4", "Decimal", "WholeNumber")
            .Decision.Should().Be(InjectionDecision.Inject);

    // ── Numeric into TextLength (Conformant / NotConformant) ──
    [Fact]
    public void NumericToTextLength_WithinBound_Inject()
        => Eval("12345", null, "TextLength", "LessThan", "10")
            .Decision.Should().Be(InjectionDecision.Inject);

    [Fact]
    public void NumericToTextLength_OverBound_Skip()
        => Eval("12345678901", null, "TextLength", "LessThan", "10")
            .Decision.Should().Be(InjectionDecision.Skip);

    // ── List-sourced text satisfying a numeric/date target (source category is neither
    //    Numeric nor DateTime, so the pre-gate never fires for a List source) ──
    [Fact]
    public void ListMemberText_SatisfyingNumericTarget_Inject()
        => Eval("5", "List", "WholeNumber", "EqualOrGreaterThan", "0")
            .Decision.Should().Be(InjectionDecision.Inject);

    // ── NotCheckable sub-classification (2b): opaque vs recognised-but-unbounded ──
    [Fact]
    public void CustomTarget_Skip_Opaque()
    {
        var r = Eval("anything", null, "Custom", targetDvFormula: "ISNUMBER(A1)");
        r.Decision.Should().Be(InjectionDecision.Skip);
        r.SkipReason.Should().Be("target data-validation rule could not be evaluated");
    }

    [Fact]
    public void UnrecognisedTargetType_Skip_Opaque()
    {
        var r = Eval("5", null, "Bogus", "EqualTo", "5");
        r.Decision.Should().Be(InjectionDecision.Skip);
        r.SkipReason.Should().Be("target data-validation rule could not be evaluated");
    }

    [Fact]
    public void RecognisedTypeNotCheckable_Unbounded_Inject()
        => Eval("7", null, "WholeNumber")
            .Decision.Should().Be(InjectionDecision.Inject);

    // ── Numeric<->date category pre-gate, both directions ──
    [Fact]
    public void PreGate_NumericSourceIntoDateTarget_Skip()
    {
        var r = Eval("45000", "WholeNumber", "Date");
        r.Decision.Should().Be(InjectionDecision.Skip);
        r.SkipReason.Should().Be("numeric value not injected into a date/time cell");
    }

    [Fact]
    public void PreGate_DateSourceIntoDecimalTarget_Skip()
    {
        var r = Eval("45000", "Date", "Decimal");
        r.Decision.Should().Be(InjectionDecision.Skip);
        r.SkipReason.Should().Be("date/time value not injected into a numeric cell");
    }

    [Fact]
    public void PreGate_DecimalSourceIntoTimeTarget_Skip()
        => Eval("0.5", "Decimal", "Time")
            .Decision.Should().Be(InjectionDecision.Skip);

    [Fact]
    public void PreGate_TimeSourceIntoWholeNumberTarget_Skip()
        => Eval("0.5", "Time", "WholeNumber")
            .Decision.Should().Be(InjectionDecision.Skip);

    // ── Pre-gate must NOT fire when sourceDvType is unknown — falls through to Evaluate ──
    [Fact]
    public void PreGate_DoesNotFire_WhenSourceDvTypeNull()
        => Eval("45000", null, "Date")
            .Decision.Should().Be(InjectionDecision.Inject);

    // ── SkipReason is location-free — callers own address idiom ──
    [Fact]
    public void SkipReasons_AreLocationFree()
    {
        var reasons = new[]
        {
            Eval("Maybe", null, "List", targetResolvedListValues: new[] { "Yes", "No" }).SkipReason,
            Eval("Yes", null, "List", targetResolvedListValues: null).SkipReason,
            Eval("anything", null, "Custom", targetDvFormula: "ISNUMBER(A1)").SkipReason,
            Eval("45000", "WholeNumber", "Date").SkipReason,
        };

        foreach (var reason in reasons)
        {
            reason.Should().NotContainAny("Row", "R1C1", "A1:", "!");
        }
    }
}
