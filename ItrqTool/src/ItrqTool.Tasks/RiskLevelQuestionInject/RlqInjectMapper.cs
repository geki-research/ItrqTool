using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.RiskLevelQuestionInject;

/// <summary>
/// Pure mapper from a CROSS-FORMAT alignment (current v02 ↔ previous v01) to the set of
/// <see cref="CellWriteEntry"/> the RLQ inject task writes into the v02 template, plus
/// messages. No I/O, no Excel, no mutation of inputs. RLQ inject is reference-only — there
/// is no carry-forward.
///
/// Per <see cref="CrossYearOutcome"/>, on Agree it emits three writes for the matched question:
///   • H→G  — the previous answer, written TYPED, gated by <see cref="InjectionValueGuard"/>
///            against the target's FULL data-validation rule (type, operator, both formulas,
///            resolved List vocabulary) — see below;
///   • K→J  — the previous current-explanation values, per-row position-aligned (text);
///   • O→P  — the previous provided-by value (text, once at the anchor row).
/// The ambiguous outcomes (XrefIdConflict / NewXrefIdWithLookalike / SameXrefIdTextDiverged)
/// emit ONE Warning and no cells; Neither / NotEvaluatedMalformedKey emit nothing.
///
/// The H→G decision (BL-053 P4b-R2) is delegated to <see cref="InjectionValueGuard.Evaluate"/>,
/// keyed on the source cell's TextValue (the DV-governed literal — never a re-stringified
/// NativeValue) against the target's DV rule:
///   1. blank source (native null, or native a blank string) → omit the G cell (no message) —
///      checked BEFORE the guard, unchanged from before.
///   2. guard Inject → write the native value. If the source/target categories are
///      WholeNumber→Decimal (a widen), ALSO emit an Info note (value written as-is).
///   3. guard Skip → SKIP the G cell, emit the guard's SkipReason at its SkipSeverity
///      (Warning or Error), CONTINUE (task still succeeds; K→J / O→P still run).
/// The native value (B1 NativeValue) is the locale-safe payload; its CLR type selects the
/// writer branch (Chunk A). The K→J and O→P writes are plain text — never typed.
/// </summary>
public static class RlqInjectMapper
{
    public static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) Map(
        CrossFormatAlignmentResult<RlqV02Question, RlqV01Question> alignment,
        RlqV02Config currentConfig,
        IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)> sourceHByRow, // keyed by v01 source RowNumber
        IReadOnlyDictionary<int, RlqTargetDvHolder> targetHByRow)                // keyed by v02 current RowNumber
    {
        var cells = new List<CellWriteEntry>();
        var messages = new List<TaskMessage>();

        foreach (var match in alignment.Matches)
        {
            var c = match.Current;

            switch (match.Outcome)
            {
                case CrossYearOutcome.Agree:
                {
                    var p = match.Previous!;

                    MapAnswer(cells, messages, c, p, currentConfig, sourceHByRow, targetHByRow);
                    MapExplanations(cells, messages, c, p, currentConfig);
                    MapProvidedBy(cells, c, p, currentConfig);

                    break;
                }

                case CrossYearOutcome.XrefIdConflict:
                case CrossYearOutcome.NewXrefIdWithLookalike:
                case CrossYearOutcome.SameXrefIdTextDiverged:
                {
                    messages.Add(new(MessageSeverity.Warning,
                        $"Row {c.RowNumber} (xref {Xref(c)}): ambiguous previous match " +
                        $"({match.Outcome}) — left untouched.",
                        DateTimeOffset.Now));
                    break;
                }

                case CrossYearOutcome.Neither:
                case CrossYearOutcome.NotEvaluatedMalformedKey:
                    // Genuine new / structural — no cells, no message.
                    break;
            }
        }

        return (cells, messages);
    }

    // ── (a) H→G — typed, gated by InjectionValueGuard against the target's DV rule ─────────────
    private static void MapAnswer(
        List<CellWriteEntry> cells,
        List<TaskMessage> messages,
        RlqV02Question c,
        RlqV01Question p,
        RlqV02Config cfg,
        IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)> sourceHByRow,
        IReadOnlyDictionary<int, RlqTargetDvHolder> targetHByRow)
    {
        sourceHByRow.TryGetValue(p.RowNumber, out var src); // (null, null, null) when absent
        targetHByRow.TryGetValue(c.RowNumber, out var targetHolder);

        var srcCat = src.DvType;
        var native = src.Native;
        var tgtCat = targetHolder?.Type;
        var g = cfg.PreviousAnswerColumn;

        // 1. blank source — checked FIRST, before any guard logic. Unchanged from before.
        if (native is null || (native is string s && string.IsNullOrWhiteSpace(s)))
            return;

        var textFallback = p.Answer ?? native.ToString() ?? "";

        // Invariant: a non-blank native value is always paired with a non-null TextValue —
        // both are read from the same source cell in BuildSourceHLookup (cell.GetString() never
        // returns null). Never fall back to a re-stringified Native.
        var decision = InjectionValueGuard.Evaluate(
            sourceText: src.TextValue!,
            sourceDvType: srcCat,
            targetDvType: targetHolder?.Type,
            targetDvOperator: targetHolder?.Operator,
            targetDvFormula: targetHolder?.Formula,
            targetDvFormula2: targetHolder?.Formula2,
            targetResolvedListValues: targetHolder?.ListValues,
            sourceNative: src.Native);

        if (decision.Decision == InjectionDecision.Inject)
        {
            cells.Add(new CellWriteEntry(c.RowNumber, g, textFallback, native));

            // Delta A: widen (WholeNumber → Decimal) is now conformant-and-injected — still
            // worth an informational note that the value was widened, not rounded/converted.
            if (IsWhole(srcCat) && IsDecimal(tgtCat))
                messages.Add(new(MessageSeverity.Info,
                    $"Row {c.RowNumber} (xref {Xref(c)}): answer type widened (WholeNumber → Decimal) — value written as-is.",
                    DateTimeOffset.Now));
        }
        else
        {
            var severity = decision.SkipSeverity == SkipSeverity.Error
                ? MessageSeverity.Error
                : MessageSeverity.Warning;
            messages.Add(new(severity,
                $"Row {c.RowNumber} (xref {Xref(c)}): {decision.SkipReason}",
                DateTimeOffset.Now));
        }
    }

    // ── (b) K→J — per-row, position/index-aligned, text ────────────────────────
    private static void MapExplanations(
        List<CellWriteEntry> cells,
        List<TaskMessage> messages,
        RlqV02Question c,
        RlqV01Question p,
        RlqV02Config cfg)
    {
        var j = cfg.PreviousExplanationColumn;
        var n = Math.Min(p.ExplanationRows.Count, c.ExplanationRows.Count);

        for (var i = 0; i < n; i++)
        {
            var src = p.ExplanationRows[i].Current;       // v01 K value
            var targetRow = c.ExplanationRows[i].RowNumber; // v02 row to write
            if (!string.IsNullOrWhiteSpace(src))
                cells.Add(new CellWriteEntry(targetRow, j, src!));
        }

        if (p.ExplanationRows.Count != c.ExplanationRows.Count)
            messages.Add(new(MessageSeverity.Warning,
                $"Row {c.RowNumber} (xref {Xref(c)}): explanation row count mismatch " +
                $"(source {p.ExplanationRows.Count}, target {c.ExplanationRows.Count}) — overlap written.",
                DateTimeOffset.Now));
    }

    // ── (c) O→P — text, once at the anchor row ─────────────────────────────────
    private static void MapProvidedBy(
        List<CellWriteEntry> cells,
        RlqV02Question c,
        RlqV01Question p,
        RlqV02Config cfg)
    {
        if (!string.IsNullOrWhiteSpace(p.ProvidedBy))
            cells.Add(new CellWriteEntry(c.RowNumber, cfg.ProvidedByColumn, p.ProvidedBy!));
    }

    private static bool IsWhole(string? cat) => cat == "WholeNumber";
    private static bool IsDecimal(string? cat) => cat == "Decimal";

    private static string Xref(RlqV02Question c) => c.XrefId ?? "<none>";
}
