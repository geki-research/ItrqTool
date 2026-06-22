namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

using System.Globalization;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

// ── CrossYearDeviationCell<T> — cross-year numeric-answer deviation primitive (finding 6a) ──
//
// Sheet-agnostic extension check: a confidently-matched answer whose NUMERIC value moved from the
// previous-year answer by at least the configured threshold is flagged. This is the first RLQ
// consumer of the cross-year arm; it mirrors CLQ_v01's AnswerDeviation, generalised off the answer
// column to any role/column and made type-aware (the answer's DV type decides whether a numeric
// comparison even applies).
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
// Present-gate + parse-gate: a blank current or previous answer is skipped (missing-answer is
// finding 1's territory); an answer that does not parse as an invariant double on either side is
// skipped (cannot compute a delta — never a false positive). The numeric parse is LOCAL and
// invariant (mirroring the evaluator's invariant-numeric convention; DvConformanceEvaluator is
// NOT imported — its Date branch stays untouched).
//
// Emits ONLY when both sides parse and |cur - prev| >= threshold. Id: cross-year.answer-deviation
// (ValidationCheck.Deviation). Each instantiated role must produce ids unique across the catalogue.

public sealed class CrossYearDeviationCell<T> : IExtensionCheck<T> where T : class, IAlignmentIdentity
{
    private readonly Func<T, string?> _answer;
    private readonly Func<T, string?> _templateDvType;
    private readonly Func<T, string?> _currentDvType;
    private readonly Func<T, string?> _providedBy;
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
        FindingEvaluation deviationDefault = FindingEvaluation.Warning)
    {
        _answer = answerSelector ?? throw new ArgumentNullException(nameof(answerSelector));
        _templateDvType = templateDvTypeSelector ?? throw new ArgumentNullException(nameof(templateDvTypeSelector));
        _currentDvType = currentDvTypeSelector ?? throw new ArgumentNullException(nameof(currentDvTypeSelector));
        _providedBy = providedBySelector ?? throw new ArgumentNullException(nameof(providedBySelector));
        if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("role must be non-empty.", nameof(role));
        if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("column must be non-empty.", nameof(column));
        _column = column;
        _threshold = threshold;
        _deviationId = "cross-year.answer-deviation";
        _descriptors = new[]
        {
            new FindingDescriptor(_deviationId, deviationDefault, ValidationCheck.Deviation,
                "The answer changed from the confidently-matched previous year by at least the configured deviation threshold."),
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

            // Parse-gate: cannot compute a delta unless both parse as invariant doubles.
            if (!TryParseInvariant(curS, out var curVal) || !TryParseInvariant(prevS, out var prevVal))
                continue;

            var delta = Math.Abs(curVal - prevVal);
            if (delta >= _threshold)
            {
                int row = cur.RowNumber;
                findings.Add(emitter.Emit(_deviationId,
                    $"{_column}{row}", cur.QuestionNumber, cur.QuestionText,
                    requestedData: null, providedBy: _providedBy(cur),
                    $"Answer at {_column}{row} deviates from the previous year by " +
                    $"{delta.ToString(CultureInfo.InvariantCulture)} (threshold " +
                    $"{_threshold.ToString(CultureInfo.InvariantCulture)}): " +
                    $"{prevS!.Trim()} -> {curS!.Trim()}."));
            }
        }
        return findings;
    }

    // WholeNumber / Decimal only (OrdinalIgnoreCase). Date/Time excluded by design (BL-022).
    private static bool IsNumericType(string? dvType) =>
        string.Equals(dvType, "WholeNumber", StringComparison.OrdinalIgnoreCase)
        || string.Equals(dvType, "Decimal", StringComparison.OrdinalIgnoreCase);

    // LOCAL invariant numeric parse (no DvConformanceEvaluator dependency). Float + thousands,
    // invariant culture — the same invariant-numeric convention the evaluator uses for bounds.
    private static bool TryParseInvariant(string? raw, out double value) =>
        double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out value);
}
