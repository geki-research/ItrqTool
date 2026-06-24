using System.Globalization;

namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

public enum DvConformanceResult
{
    Conformant,       // the value satisfies the DV rule
    NotConformant,    // the value violates the DV rule (a real finding)
    UnresolvableList, // List type, vocabulary did not resolve (null) — a real gap to surface (BL-025)
    NotCheckable      // the rule cannot be evaluated here (no constraint, Custom formula,
                      // missing bound, or an unknown type) — never a finding
}

/// <summary>
/// Pure, IO-free typed-conformance core for finding 5 (DV-conformance): does a single PRESENT
/// value satisfy a data-validation rule? Dispatches by DV type (the ClosedXML
/// <c>XLAllowedValues</c> name carried on <c>ExcelCellStructure.DataValidationType</c>) and applies
/// operator semantics for the numeric / date / text-length types.
/// <para>
/// Conservative posture (CLAUDE.md "when in doubt, surface" but NEVER false-positive a value that
/// cannot be judged): a value that fails its TYPE constraint (e.g. a non-integer under WholeNumber)
/// is <see cref="DvConformanceResult.NotConformant"/>; anything the evaluator genuinely cannot
/// decide — no constraint, an unresolved List source, a Custom formula, a missing/unparseable
/// bound, or an unknown type — is <see cref="DvConformanceResult.NotCheckable"/> (skipped), not a
/// finding.
/// </para>
/// <para>
/// Comparisons are unified through OADate-style doubles: WholeNumber/Decimal parse the value
/// directly, TextLength uses the value length, Date/Time convert to an OADate serial (reusing the
/// <c>double.TryParse → DateTime.FromOADate</c> idiom from <c>DvDisplayFormatter.FormatValue</c>;
/// see the date-parsing note below). Bound formulas (<c>dvFormula</c>, <c>dvFormula2</c>) are read
/// as invariant doubles — for Date/Time these are the OADate serials ClosedXML stores.
/// </para>
/// </summary>
public static class DvConformanceEvaluator
{
    /// <param name="value">The present value (caller guarantees non-blank via the present-gate).</param>
    /// <param name="dvType">DV type — ClosedXML <c>XLAllowedValues</c> name, or null for no rule.</param>
    /// <param name="dvOperator">DV operator (null for List/Custom/AnyValue).</param>
    /// <param name="dvFormula">Bound 1 (lower bound for Between/NotBetween).</param>
    /// <param name="dvFormula2">Bound 2 (upper bound — Between/NotBetween only).</param>
    /// <param name="resolvedListValues">
    /// For a List source: the already-resolved allowed values (inline parsed, or range/named
    /// resolved in the patch phase). Non-null ⇒ membership is checked. Null ⇒ unresolved ⇒
    /// <see cref="DvConformanceResult.UnresolvableList"/> (a real gap to surface — BL-025).
    /// </param>
    public static DvConformanceResult Evaluate(
        string value,
        string? dvType,
        string? dvOperator,
        string? dvFormula,
        string? dvFormula2,
        IReadOnlyList<string>? resolvedListValues)
    {
        // No type ⇒ no constraint (mirrors AnyValue).
        if (string.IsNullOrEmpty(dvType)) return DvConformanceResult.Conformant;

        if (TypeIs(dvType, "AnyValue")) return DvConformanceResult.Conformant;

        if (TypeIs(dvType, "List"))
        {
            if (resolvedListValues is null) return DvConformanceResult.UnresolvableList;   // was NotCheckable (BL-025)
            return resolvedListValues.Any(v =>
                       string.Equals(v.Trim(), value.Trim(), StringComparison.Ordinal))
                ? DvConformanceResult.Conformant
                : DvConformanceResult.NotConformant;
        }

        if (TypeIs(dvType, "Custom")) return DvConformanceResult.NotCheckable;

        if (TypeIs(dvType, "WholeNumber"))
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return DvConformanceResult.NotConformant;   // type violation
            return ApplyNumericOperator(n, dvOperator, dvFormula, dvFormula2);
        }

        if (TypeIs(dvType, "Decimal"))
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return DvConformanceResult.NotConformant;   // type violation
            return ApplyNumericOperator(d, dvOperator, dvFormula, dvFormula2);
        }

        if (TypeIs(dvType, "TextLength"))
            return ApplyNumericOperator(value.Length, dvOperator, dvFormula, dvFormula2);

        if (TypeIs(dvType, "Date") || TypeIs(dvType, "Time"))
        {
            if (!TryGetDateSerial(value, out var serial))
                return DvConformanceResult.NotConformant;   // type violation
            return ApplyNumericOperator(serial, dvOperator, dvFormula, dvFormula2);
        }

        // Unknown / unrecognised type string — be conservative, never false-positive.
        return DvConformanceResult.NotCheckable;
    }

    // Applies the DV operator on a comparable double. A missing operator or a missing/unparseable
    // bound the operator needs ⇒ NotCheckable (cannot judge — NOT a false NotConformant).
    private static DvConformanceResult ApplyNumericOperator(
        double value, string? op, string? f1, string? f2)
    {
        if (string.IsNullOrEmpty(op)) return DvConformanceResult.NotCheckable;
        if (!TryParseInvariant(f1, out var b1)) return DvConformanceResult.NotCheckable;

        bool needsTwo = OpEquals(op, "Between") || OpEquals(op, "NotBetween");
        double b2 = 0;
        if (needsTwo && !TryParseInvariant(f2, out b2)) return DvConformanceResult.NotCheckable;

        bool? ok =
            OpEquals(op, "EqualTo")            ? value == b1 :
            OpEquals(op, "NotEqualTo")         ? value != b1 :
            OpEquals(op, "GreaterThan")        ? value > b1 :
            OpEquals(op, "LessThan")           ? value < b1 :
            OpEquals(op, "EqualOrGreaterThan") ? value >= b1 :
            OpEquals(op, "EqualOrLessThan")    ? value <= b1 :
            OpEquals(op, "Between")            ? value >= Math.Min(b1, b2) && value <= Math.Max(b1, b2) :
            OpEquals(op, "NotBetween")         ? value < Math.Min(b1, b2) || value > Math.Max(b1, b2) :
            (bool?)null;   // unknown operator

        if (ok is null) return DvConformanceResult.NotCheckable;
        return ok.Value ? DvConformanceResult.Conformant : DvConformanceResult.NotConformant;
    }

    // Date-parsing note: the value comes from cell.GetString(), which may be an OADate serial OR a
    // formatted date string depending on the cell. We accept both — a serial first (matching how
    // DvDisplayFormatter reads date bounds), else an invariant DateTime parse → ToOADate(). Bounds
    // are read as invariant doubles (the OADate serials ClosedXML stores for Date/Time DV).
    private static bool TryGetDateSerial(string value, out double serial)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out serial))
            return true;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            serial = dt.ToOADate();
            return true;
        }
        serial = 0;
        return false;
    }

    private static bool TryParseInvariant(string? raw, out double value) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TypeIs(string? dvType, string name) =>
        string.Equals(dvType, name, StringComparison.OrdinalIgnoreCase);

    private static bool OpEquals(string? op, string name) =>
        string.Equals(op, name, StringComparison.OrdinalIgnoreCase);
}
