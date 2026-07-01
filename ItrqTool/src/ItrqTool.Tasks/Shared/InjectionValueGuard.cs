using ItrqTool.Tasks.QuestionnaireValidation.Checks;

namespace ItrqTool.Tasks.Shared;

public enum InjectionDecision { Inject, Skip }

// SkipReason is LOCATION-FREE — the caller prepends its own address idiom and emits the
// TaskMessage at Warning.
public sealed record InjectionCheckResult(InjectionDecision Decision, string? SkipReason);

/// <summary>
/// Pure, IO-free gate: should <paramref name="sourceText"/>(as passed to
/// <see cref="Evaluate"/>) be injected into a target cell, given the target's data-validation
/// rule? Wraps <see cref="DvConformanceEvaluator"/> and adds one category-based pre-gate for
/// numeric-vs-date confusion that the evaluator's value-only check cannot catch (a numeric
/// magnitude can coincidentally fall in a plausible OADate range).
/// </summary>
public static class InjectionValueGuard
{
    private static readonly HashSet<string> NumericTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "WholeNumber", "Decimal"
    };

    private static readonly HashSet<string> DateTimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Date", "Time"
    };

    private static readonly HashSet<string> RecognisedValueTypedRules = new(StringComparer.OrdinalIgnoreCase)
    {
        "WholeNumber", "Decimal", "TextLength", "Date", "Time"
    };

    /// <param name="sourceText">
    /// The value proposed for injection — the source cell's TextValue (the literal captured text
    /// reflecting original DV-governed formatting). NEVER pass a re-stringified NativeValue: a
    /// double round-trip can change culture / ".0" / separators and parse differently. Callers
    /// must omit blank sources before calling; a non-blank value is assumed.
    /// </param>
    /// <param name="sourceDvType">
    /// The source cell's DV category string, if known (RLQ/GD supply it); null when unknown
    /// (CellRangeInject/CLQ). Used only for the numeric&lt;-&gt;date category pre-gate.
    /// </param>
    /// <param name="targetResolvedListValues">
    /// Pre-resolved by the caller (inline-parsed or via range/named-range resolution); null means
    /// the target is a List whose vocabulary could not be resolved. This method performs no I/O
    /// and no list resolution itself.
    /// </param>
    public static InjectionCheckResult Evaluate(
        string sourceText,
        string? sourceDvType,
        string? targetDvType, string? targetDvOperator, string? targetDvFormula, string? targetDvFormula2,
        IReadOnlyList<string>? targetResolvedListValues)
    {
        if (sourceDvType is not null)
        {
            if (NumericTypes.Contains(sourceDvType) && targetDvType is not null && DateTimeTypes.Contains(targetDvType))
                return new InjectionCheckResult(InjectionDecision.Skip, "numeric value not injected into a date/time cell");

            if (DateTimeTypes.Contains(sourceDvType) && targetDvType is not null && NumericTypes.Contains(targetDvType))
                return new InjectionCheckResult(InjectionDecision.Skip, "date/time value not injected into a numeric cell");
        }

        var result = DvConformanceEvaluator.Evaluate(
            sourceText, targetDvType, targetDvOperator, targetDvFormula, targetDvFormula2, targetResolvedListValues);

        switch (result)
        {
            case DvConformanceResult.Conformant:
                return new InjectionCheckResult(InjectionDecision.Inject, null);

            case DvConformanceResult.NotConformant:
                var ruleText = DvDisplayFormatter.FormatFull(targetDvType, targetDvOperator, targetDvFormula, targetDvFormula2);
                var reason = ruleText == "—"
                    ? $"value '{sourceText}' does not conform to the target data-validation rule"
                    : $"value '{sourceText}' does not conform to the target data-validation rule ({ruleText})";
                return new InjectionCheckResult(InjectionDecision.Skip, reason);

            case DvConformanceResult.UnresolvableList:
                return new InjectionCheckResult(InjectionDecision.Skip, "target data-validation vocabulary could not be resolved");

            case DvConformanceResult.NotCheckable:
                if (targetDvType is not null && RecognisedValueTypedRules.Contains(targetDvType))
                    return new InjectionCheckResult(InjectionDecision.Inject, null);
                return new InjectionCheckResult(InjectionDecision.Skip, "target data-validation rule could not be evaluated");

            default:
                return new InjectionCheckResult(InjectionDecision.Skip, "target data-validation rule could not be evaluated");
        }
    }
}
