using System.Globalization;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

namespace ItrqTool.Tasks.GeneralDataValidationV01;

// ── GdAnswerDeviationCell — cross-year numeric deviation at ANSWER grain ──
//
// The GD per-answer analogue of CrossYearDeviationCell<T> (which is at question grain and reads
// the answer value from a selector on the question). GD needs deviation at answer grain because
// each question has N answers, each requiring its own current↔previous pair comparison.
//
// Gate: JoinedByXrefId (mirrors GdAnswerFrozenConstraintCell) plus the implicit Agree gate via
// ToPrevious — GdAnswerJoin.ToPrevious returns null counterparts for non-Agree outcomes, and
// null counterparts are skipped. Only answers where both sides parse as numeric and the previous
// is non-zero are compared.
//
// Numeric kernel DUPLICATED from CrossYearDeviationCell<T> (lesson B2 — do NOT extract):
//   IsNumericType, TryParseInvariant, and the relChange >= _threshold comparison are verbatim
//   private static copies. The numeric kernel is LOCAL — no DvConformanceEvaluator dependency.
//
// Finding id + Check reused from CrossYearDeviationCell (lesson 107):
//   cross-year.answer-deviation — ValidationCheck.Deviation, Warning default.
//
// Emit: GdPerAnswerEmit.CellAddress(column, currentAnswer) — the CURRENT answer's anchor row.

public sealed class GdAnswerDeviationCell : IExtensionCheck<GdV01Question>
{
    private readonly Func<GdAnswer, string?> _currentValue;
    private readonly Func<GdAnswer, string?> _previousValue;
    private readonly Func<GdAnswer, string?> _dvType;
    private readonly Func<GdAnswer, string?> _providedBy;
    private readonly Func<GdAnswer, object?>? _currentNative;
    private readonly Func<GdAnswer, object?>? _previousNative;
    private readonly string _column;
    private readonly double _threshold;
    private readonly string _deviationId;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public GdAnswerDeviationCell(
        Func<GdAnswer, string?> currentValueSelector,
        Func<GdAnswer, string?> previousValueSelector,
        Func<GdAnswer, string?> dvTypeSelector,
        Func<GdAnswer, string?> providedBySelector,
        string column,
        double threshold,
        FindingEvaluation deviationDefault = FindingEvaluation.Warning,
        Func<GdAnswer, object?>? currentNativeSelector = null,
        Func<GdAnswer, object?>? previousNativeSelector = null)
    {
        _currentValue  = currentValueSelector  ?? throw new ArgumentNullException(nameof(currentValueSelector));
        _previousValue = previousValueSelector ?? throw new ArgumentNullException(nameof(previousValueSelector));
        _dvType        = dvTypeSelector        ?? throw new ArgumentNullException(nameof(dvTypeSelector));
        _providedBy    = providedBySelector    ?? throw new ArgumentNullException(nameof(providedBySelector));
        // Optional — null means "not wired", keeping the pure text-parse path byte-for-byte. GD
        // separates the current/previous VALUE selectors (unlike RLQ's single selector for both
        // sides), so the native selectors are PAIRED to match: each side resolves with its own.
        _currentNative  = currentNativeSelector;
        _previousNative = previousNativeSelector;
        if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("column must be non-empty.", nameof(column));
        _column       = column;
        _threshold    = threshold;
        _deviationId  = "cross-year.answer-deviation";
        _descriptors  = new[]
        {
            new FindingDescriptor(_deviationId, deviationDefault, ValidationCheck.Deviation,
                "The answer changed from the confidently-matched previous year by at least the configured relative (percentage) deviation threshold."),
        };
    }

    public IReadOnlyList<FindingDescriptor> Descriptors => _descriptors;

    public IReadOnlyList<ValidationFinding> Run(AlignmentResult<GdV01Question> alignment, FindingEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(emitter);

        var findings = new List<ValidationFinding>();
        foreach (var aq in alignment.Aligned)
        {
            // Deviation needs template context (JoinedByXrefId) and a confident previous baseline
            // (Agree, surfaced via ToPrevious null counterparts). Both gates are satisfied together:
            // JoinedByXrefId skips AddedInResponse/MalformedKey; ToPrevious null-pair skip covers
            // non-Agree cross-year outcomes.
            if (aq.WithinYear != WithinYearJoin.JoinedByXrefId)
                continue;

            var cur = aq.Current;
            foreach (var pair in GdAnswerJoin.ToPrevious(aq))
            {
                if (pair.Counterpart is null)
                    continue;   // no previous counterpart (non-Agree cross-year) → skip

                var answer     = pair.Current;
                var prevAnswer = pair.Counterpart;

                // Numeric-only: the answer's DV type must be WholeNumber or Decimal.
                if (!IsNumericType(_dvType(answer)))
                    continue;

                var curS  = _currentValue(answer);
                var prevS = _previousValue(prevAnswer);

                // Present-gate: blank on either side is not a deviation finding.
                if (string.IsNullOrWhiteSpace(curS) || string.IsNullOrWhiteSpace(prevS))
                    continue;

                // Resolve-gate, PER SIDE: prefer the cell's own native double (via the matching
                // paired selector); fall back to the invariant text parse. Cannot compute a
                // deviation unless BOTH sides resolve. Per-side (not all-or-nothing) mirrors
                // CrossYearDeviationCell<T>: native is the workbook's own typed value, text a lossy
                // rendering of it, so per-side is strictly more correct in the mixed-rendering case.
                if (!TryResolve(_currentNative, answer, curS, out var curVal)
                    || !TryResolve(_previousNative, prevAnswer, prevS, out var prevVal))
                    continue;

                // Zero-base gate: relative change undefined when previous is zero.
                if (prevVal == 0)
                    continue;

                // RELATIVE deviation: |cur - prev| / |prev|. >= is inclusive.
                var relChange = Math.Abs(curVal - prevVal) / Math.Abs(prevVal);
                if (relChange >= _threshold)
                {
                    var cell = GdPerAnswerEmit.CellAddress(_column, answer);
                    findings.Add(emitter.Emit(_deviationId,
                        cell, cur.QuestionNumber, cur.QuestionText,
                        requestedData: null, providedBy: _providedBy(answer),
                        $"Answer at {cell} changed from {prevS!.Trim()} to {curS!.Trim()} " +
                        $"({relChange.ToString("P0", CultureInfo.InvariantCulture)}), exceeding the " +
                        $"{_threshold.ToString("P0", CultureInfo.InvariantCulture)} year-over-year deviation threshold."));
                }
            }
        }
        return findings;
    }

    // WholeNumber / Decimal only (OrdinalIgnoreCase). Date/Time excluded by design (BL-022).
    // Duplicated verbatim from CrossYearDeviationCell<T> — do NOT extract (lesson B2).
    private static bool IsNumericType(string? dvType) =>
        string.Equals(dvType, "WholeNumber", StringComparison.OrdinalIgnoreCase)
        || string.Equals(dvType, "Decimal", StringComparison.OrdinalIgnoreCase);

    // Resolves ONE side to a comparable double: the cell's native value (via the side's paired
    // selector) when it is a finite double, else the invariant text parse. Returns false only when
    // NEITHER yields a number. This wrapper is the ONLY intended divergence from
    // CrossYearDeviationCell<T>: RLQ threads a single _native selector for both sides, GD passes the
    // matching paired selector per side. The IsFinite guard is mandatory — a NaN/∞ native must fall
    // through to the text parse rather than poison relChange.
    private static bool TryResolve(Func<GdAnswer, object?>? native, GdAnswer record, string? text, out double value)
    {
        if (native?.Invoke(record) is double n && double.IsFinite(n))
        {
            value = n;
            return true;
        }
        return TryParseInvariant(text, out value);
    }

    // LOCAL invariant numeric parse (no DvConformanceEvaluator dependency). Float + thousands,
    // invariant culture — the same invariant-numeric convention the evaluator uses for bounds.
    // Duplicated verbatim from CrossYearDeviationCell<T> — do NOT extract (lesson B2).
    private static bool TryParseInvariant(string? raw, out double value) =>
        double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out value);
}
