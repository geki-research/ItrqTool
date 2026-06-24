using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;

namespace ItrqTool.Tasks.RiskLevelQuestionInject;

/// <summary>
/// Pure mapper from a CROSS-FORMAT alignment (current v02 ↔ previous v01) to the set of
/// <see cref="CellWriteEntry"/> the RLQ inject task writes into the v02 template, plus
/// messages. No I/O, no Excel, no mutation of inputs. RLQ inject is reference-only — there
/// is no carry-forward.
///
/// Per <see cref="CrossYearOutcome"/>, on Agree it emits three writes for the matched question:
///   • H→G  — the previous answer, written TYPED per the answer type-compatibility policy
///            (equal / widen / narrow / incompatible — see below);
///   • K→J  — the previous current-explanation values, per-row position-aligned (text);
///   • O→P  — the previous provided-by value (text, once at the anchor row).
/// The ambiguous outcomes (XrefIdConflict / NewXrefIdWithLookalike / SameXrefIdTextDiverged)
/// emit ONE Warning and no cells; Neither / NotEvaluatedMalformedKey emit nothing.
///
/// The H→G TYPE-COMPATIBILITY POLICY keys on the SOURCE (v01 H) and TARGET (v02 H)
/// data-validation categories — NOT the native CLR type, which cannot distinguish
/// WholeNumber from Decimal (ClosedXML surfaces every numeric as a double):
///   1. blank source (native null, or native a blank string) → omit the G cell (no message).
///   2. srcCat == tgtCat (string-equal, incl. both null / both List / both numeric) → EQUAL:
///      write the native value, no message.
///   3. WholeNumber → Decimal → WIDEN: write the native value, + Warning.
///   4. Decimal → WholeNumber → NARROW: write the native value AS-IS (no rounding), + Warning.
///   5. any other pair → MISMATCH: Error message, SKIP the G cell, CONTINUE (task still succeeds).
/// The native value (B1 NativeValue) is the locale-safe payload; its CLR type selects the
/// writer branch (Chunk A). The K→J and O→P writes are plain text — never typed.
/// </summary>
public static class RlqInjectMapper
{
    public static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) Map(
        CrossFormatAlignmentResult<RlqV02Question, RlqV01Question> alignment,
        RlqV02Config currentConfig,
        IReadOnlyDictionary<int, (string? DvType, object? Native)> sourceHByRow, // keyed by v01 source RowNumber
        IReadOnlyDictionary<int, string?> targetHByRow)                          // keyed by v02 current RowNumber
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

    // ── (a) H→G — typed, per the answer type-compatibility policy ──────────────
    private static void MapAnswer(
        List<CellWriteEntry> cells,
        List<TaskMessage> messages,
        RlqV02Question c,
        RlqV01Question p,
        RlqV02Config cfg,
        IReadOnlyDictionary<int, (string? DvType, object? Native)> sourceHByRow,
        IReadOnlyDictionary<int, string?> targetHByRow)
    {
        sourceHByRow.TryGetValue(p.RowNumber, out var src); // (null, null) when absent
        targetHByRow.TryGetValue(c.RowNumber, out var tgtCat);

        var srcCat = src.DvType;
        var native = src.Native;
        var g = cfg.PreviousAnswerColumn;

        // 1. blank source — checked FIRST, before any type logic.
        if (native is null || (native is string s && string.IsNullOrWhiteSpace(s)))
            return;

        var textFallback = p.Answer ?? native.ToString() ?? "";

        // 2. equal category (string-equal, incl. both null / both List / both numeric).
        if (string.Equals(srcCat, tgtCat, StringComparison.Ordinal))
        {
            cells.Add(new CellWriteEntry(c.RowNumber, g, textFallback, native));
            return;
        }

        // 3. widen: WholeNumber → Decimal.
        if (IsWhole(srcCat) && IsDecimal(tgtCat))
        {
            cells.Add(new CellWriteEntry(c.RowNumber, g, textFallback, native));
            messages.Add(new(MessageSeverity.Warning,
                $"Row {c.RowNumber} (xref {Xref(c)}): answer type widened (WholeNumber → Decimal) — value written as-is.",
                DateTimeOffset.Now));
            return;
        }

        // 4. narrow: Decimal → WholeNumber — written AS-IS (no rounding).
        if (IsDecimal(srcCat) && IsWhole(tgtCat))
        {
            cells.Add(new CellWriteEntry(c.RowNumber, g, textFallback, native));
            messages.Add(new(MessageSeverity.Warning,
                $"Row {c.RowNumber} (xref {Xref(c)}): answer type narrowed (Decimal → WholeNumber) — value written as-is (not rounded).",
                DateTimeOffset.Now));
            return;
        }

        // 5. mismatch: any other pair — Error, skip the G cell, continue.
        messages.Add(new(MessageSeverity.Error,
            $"Row {c.RowNumber} (xref {Xref(c)}): incompatible answer types " +
            $"(source {Cat(srcCat)} → target {Cat(tgtCat)}) — previous answer not injected.",
            DateTimeOffset.Now));
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
    private static string Cat(string? cat) => cat ?? "<none>";
}
