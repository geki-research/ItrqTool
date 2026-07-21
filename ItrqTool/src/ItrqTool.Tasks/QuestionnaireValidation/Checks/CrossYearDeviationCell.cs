namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

using System.Globalization;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

// ── CrossYearDeviationCell<T> — cross-year numeric-answer deviation primitive (finding 6a) ──
//
// Sheet-agnostic extension check: a confidently-matched answer whose NUMERIC value moved from the
// previous-year answer by at least the configured RELATIVE (percentage) threshold is flagged. This
// is the first RLQ consumer of the cross-year arm; it mirrors CLQ_v01's AnswerDeviation, generalised
// off the answer column to any role/column and made type-aware (the answer's DV type decides whether
// a numeric comparison even applies).
//
// Gate: cross-year deviation is judged against the previous-RESPONSE match, so only Agree rows are
// evaluated — AlignedQuestion<T>.PreviousMatch is non-null IFF CrossYear == Agree. Every other
// outcome (XrefIdConflict / NewXrefIdWithLookalike / SameXrefIdTextDiverged / Neither /
// NotEvaluatedMalformedKey) is skipped: no confident baseline, no deviation.
//
// Numeric-only: the comparison runs ONLY when the answer's DV type is WholeNumber or Decimal
// (read from the TEMPLATE match's frozen DV, falling back to the current's). List / Text / Date /
// Time / AnyValue / Custom / no-type are skipped — Date is deliberately out of scope here (BL-022).
//
// Present-gate + resolve-gate: a blank current or previous answer is skipped (missing-answer is
// finding 1's territory); a side that resolves to no number at all is skipped (cannot compute a
// deviation — never a false positive). Each side resolves INDEPENDENTLY: the cell's native double
// when one is present and finite, else the LOCAL invariant text parse (mirroring the evaluator's
// invariant-numeric convention; DvConformanceEvaluator is NOT imported — its Date branch stays
// untouched). Native precedence is what keeps a comma-decimal answer from being silently consumed
// as a thousands separator; see TryResolve.
//
// Zero-base gate: a previous-year answer of 0 is skipped — a relative change has no percentage base.
//
// Emits ONLY when both sides parse, prev != 0, and |cur - prev| / |prev| >= threshold (the threshold
// is a FRACTION; >= is inclusive). Id: cross-year.answer-deviation (ValidationCheck.Deviation). Each
// instantiated role must produce ids unique across the catalogue.

public sealed class CrossYearDeviationCell<T> : IExtensionCheck<T> where T : class, IAlignmentIdentity
{
    private readonly Func<T, string?> _answer;
    private readonly Func<T, string?> _templateDvType;
    private readonly Func<T, string?> _currentDvType;
    private readonly Func<T, string?> _providedBy;
    private readonly Func<T, object?>? _native;
    private readonly string _column;
    private readonly double _threshold;
    private readonly string _deviationId;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public CrossYearDeviationCell(
        Func<T, string?> answerSelector,
        Func<T, string?> templateDvTypeSelector,
        Func<T, string?> currentDvTypeSelector,
        Func<T, string?> providedBySelector,
        string role,
        string column,
        double threshold,
        FindingEvaluation deviationDefault = FindingEvaluation.Warning,
        Func<T, object?>? nativeSelector = null)
    {
        _answer = answerSelector ?? throw new ArgumentNullException(nameof(answerSelector));
        _templateDvType = templateDvTypeSelector ?? throw new ArgumentNullException(nameof(templateDvTypeSelector));
        _currentDvType = currentDvTypeSelector ?? throw new ArgumentNullException(nameof(currentDvTypeSelector));
        _providedBy = providedBySelector ?? throw new ArgumentNullException(nameof(providedBySelector));
        // Optional — null means "not wired", which keeps the pure text-parse path byte-for-byte.
        // ONE selector serves BOTH sides: cur and prev are the same record type, and the cell
        // already applies its single _answer selector to both.
        _native = nativeSelector;
        if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("role must be non-empty.", nameof(role));
        if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("column must be non-empty.", nameof(column));
        _column = column;
        _threshold = threshold;
        _deviationId = "cross-year.answer-deviation";
        _descriptors = new[]
        {
            new FindingDescriptor(_deviationId, deviationDefault, ValidationCheck.Deviation,
                "The answer changed from the confidently-matched previous year by at least the configured relative (percentage) deviation threshold."),
        };
    }

    public IReadOnlyList<FindingDescriptor> Descriptors => _descriptors;

    public IReadOnlyList<ValidationFinding> Run(AlignmentResult<T> alignment, FindingEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(emitter);

        var findings = new List<ValidationFinding>();
        foreach (var aq in alignment.Aligned)
        {
            // Deviation needs a confident previous-year baseline → only Agree rows.
            // (PreviousMatch is non-null IFF CrossYear == Agree.)
            if (aq.CrossYear != CrossYearOutcome.Agree)
                continue;

            var cur = aq.Current;
            var prev = aq.PreviousMatch!; // non-null IFF Agree

            // Numeric-only: the answer's DV type (template-frozen, current fallback) must be a
            // numeric type. List / Text / Date / Time / AnyValue / Custom / none → skip.
            // TemplateMatch is null when the row is Agree cross-year but AddedInResponse within-year
            // (no template side) — fall back to the current's own answer-DV type.
            var dvType = (aq.TemplateMatch is { } tmpl ? _templateDvType(tmpl) : null)
                         ?? _currentDvType(cur);
            if (!IsNumericType(dvType))
                continue;

            var curS = _answer(cur);
            var prevS = _answer(prev);

            // Present-gate: a blank on either side is finding 1's concern, never reported here.
            if (string.IsNullOrWhiteSpace(curS) || string.IsNullOrWhiteSpace(prevS))
                continue;

            // Resolve-gate, PER SIDE: prefer the cell's own native double; fall back to the
            // invariant text parse. Cannot compute a deviation unless BOTH sides resolve.
            //
            // Per-side (not all-or-nothing) is deliberate: requiring both sides to carry a native
            // before trusting either would revert a correct native operand to a corrupt text one
            // in exactly the mixed-rendering case that does the most damage. Native is the
            // workbook's own typed value; text is a lossy rendering of it — so per-side is
            // strictly more correct and never less.
            if (!TryResolve(cur, curS, out var curVal) || !TryResolve(prev, prevS, out var prevVal))
                continue;

            // No percentage base when the previous-year answer is zero: skip. A relative change is
            // undefined against a zero base — this covers prev=0/cur=0 (no change) AND prev=0/cur!=0
            // (no base to measure against), both per the settled spec.
            if (prevVal == 0)
                continue;

            // RELATIVE deviation: |cur - prev| / |prev|. The threshold is a FRACTION (0.25 = 25%);
            // the comparison is inclusive (>=), so exactly-threshold flags. The |prev| denominator
            // handles a negative previous value.
            var relChange = Math.Abs(curVal - prevVal) / Math.Abs(prevVal);
            if (relChange >= _threshold)
            {
                int row = cur.RowNumber;
                findings.Add(emitter.Emit(_deviationId,
                    $"{_column}{row}", cur.QuestionNumber, cur.QuestionText,
                    requestedData: null, providedBy: _providedBy(cur),
                    $"Answer at {_column}{row} changed from {prevS!.Trim()} to {curS!.Trim()} " +
                    $"({relChange.ToString("P0", CultureInfo.InvariantCulture)}), exceeding the " +
                    $"{_threshold.ToString("P0", CultureInfo.InvariantCulture)} year-over-year deviation threshold."));
            }
        }
        return findings;
    }

    // WholeNumber / Decimal only (OrdinalIgnoreCase). Date/Time excluded by design (BL-022).
    private static bool IsNumericType(string? dvType) =>
        string.Equals(dvType, "WholeNumber", StringComparison.OrdinalIgnoreCase)
        || string.Equals(dvType, "Decimal", StringComparison.OrdinalIgnoreCase);

    // Resolves ONE side to a comparable double: the cell's native value when it is a finite double,
    // else the invariant text parse. Returns false only when NEITHER yields a number.
    //
    // Why native takes precedence: TryParseInvariant below carries AllowThousands, and under
    // invariant culture the group separator is ',' — so a European decimal comma is not rejected but
    // SILENTLY CONSUMED ("9,1" → 91). That corrupts the operands of a relative-change computation
    // rather than failing it, which is why the native is preferred wherever the workbook supplies one.
    //
    // The IsFinite guard is mandatory: a NaN/∞ native must fall through to the text parse rather
    // than poison relChange (mirrors DvConformanceEvaluator's native branches).
    private bool TryResolve(T record, string? text, out double value)
    {
        if (_native?.Invoke(record) is double n && double.IsFinite(n))
        {
            value = n;
            return true;
        }
        return TryParseInvariant(text, out value);
    }

    // LOCAL invariant numeric parse (no DvConformanceEvaluator dependency). Float + thousands,
    // invariant culture — the same invariant-numeric convention the evaluator uses for bounds.
    // DELIBERATELY left byte-identical, AllowThousands included: dropping the flag would reject
    // legitimately grouped invariant input ("1,713,402.75") that parses today — a separate decision
    // with its own blast radius. This is the unchanged fallback; native merely takes precedence.
    private static bool TryParseInvariant(string? raw, out double value) =>
        double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out value);
}
