using ItrqTool.Domain;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.GeneralDataValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;

namespace ItrqTool.Tasks.GeneralDataInject;

/// <summary>
/// Pure mapper from a CROSS-VERSION alignment (current v02 ↔ previous v01) to the set of
/// <see cref="CellWriteEntry"/> the GD inject task writes into the v02 template, plus
/// messages. No I/O, no Excel, no mutation of inputs. GD inject is reference-only — there
/// is no carry-forward.
///
/// It is the per-ANSWER-grain analog of <c>RlqInjectMapper</c>: RLQ acts per question (a
/// question IS one answer); GD acts per ANSWER inside a per-question loop, pairing current
/// (v02) answers to previous (v01) answers by <c>AnswerId</c> (inline <see cref="PairByAnswerId"/>,
/// not the frozen single-type <c>GdAnswerJoin</c>). For each paired answer with a non-null
/// counterpart it emits three writes:
///   • H→G  — the previous answer, written TYPED per the answer type-compatibility policy
///            (equal / widen / narrow / incompatible — see below), at the v02 answer anchor row;
///   • K→J  — the previous current-explanation values, per explanation-row position-aligned (text);
///   • O→P  — the previous provided-by value (text, once at the v02 answer anchor row).
/// M (how-explanation) is NEVER written — reference injection writes only the previous-* columns.
/// A current answer with no v01 counterpart is left untouched (no writes).
///
/// The ambiguous outcomes (XrefIdConflict / NewXrefIdWithLookalike / SameXrefIdTextDiverged)
/// emit ONE Warning per question and no cells; Neither / NotEvaluatedMalformedKey emit nothing.
///
/// The H→G TYPE-COMPATIBILITY POLICY keys on the SOURCE (v01 H) and TARGET (v02 H)
/// data-validation categories — NOT the native CLR type, which cannot distinguish
/// WholeNumber from Decimal (ClosedXML surfaces every numeric as a double):
///   1. blank source (native null, or native a blank string) → omit the G cell (no message).
///   2. srcCat == tgtCat (string-equal, incl. both null / both List / both numeric) → EQUAL:
///      write the native value, no message.
///   3. WholeNumber → Decimal → WIDEN: write the native value, + Warning.
///   4. Decimal → WholeNumber → NARROW: write the native value AS-IS (no rounding), + Warning.
///   5. any other pair → MISMATCH: Error message, SKIP the G cell, CONTINUE (task still succeeds;
///      the same answer's K→J and O→P writes still fire).
/// The DV lookups are keyed by ANSWER AnchorRow (source by v01 anchor, target by v02 anchor).
/// </summary>
public static class GdInjectMapper
{
    public static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) Map(
        CrossFormatAlignmentResult<GdV02Question, GdV01Question> alignment,
        GdV02Config currentConfig,
        IReadOnlyDictionary<int, (string? DvType, object? Native)> sourceHByAnchorRow, // v01 answer AnchorRow → H DV + native
        IReadOnlyDictionary<int, string?> targetHByAnchorRow)                          // v02 answer AnchorRow → H DV-type
    {
        var cells = new List<CellWriteEntry>();
        var messages = new List<TaskMessage>();

        foreach (var match in alignment.Matches)
        {
            var cq = match.Current;

            switch (match.Outcome)
            {
                case CrossYearOutcome.Agree:
                {
                    var pq = match.Previous!;

                    foreach (var (ca, pa) in PairByAnswerId(cq.Answers, pq.Answers))
                    {
                        if (pa is null)
                            continue; // current answer with no v01 counterpart → untouched.

                        MapAnswer(cells, messages, cq, ca, pa, currentConfig, sourceHByAnchorRow, targetHByAnchorRow);
                        MapExplanations(cells, messages, cq, ca, pa, currentConfig);
                        MapProvidedBy(cells, ca, pa, currentConfig);
                    }

                    break;
                }

                case CrossYearOutcome.XrefIdConflict:
                case CrossYearOutcome.NewXrefIdWithLookalike:
                case CrossYearOutcome.SameXrefIdTextDiverged:
                {
                    messages.Add(new(MessageSeverity.Warning,
                        $"Question {Xref(cq)} (row {cq.RowNumber}): ambiguous previous match " +
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

    // ── per-answer pairing by AnswerId (inline 2-type equivalent of GdAnswerJoin) ──
    // key = AnswerId ?? "" (the bare-qid collapsed answer keys to ""); AnswerIds are unique
    // within a question, so a defensive first-wins TryAdd tolerates any unexpected dup. One
    // pair per CURRENT answer, current order preserved; a null counterpart → Prev == null.
    private static IReadOnlyList<(GdV02Answer Cur, GdAnswer? Prev)> PairByAnswerId(
        IReadOnlyList<GdV02Answer> current,
        IReadOnlyList<GdAnswer> previous)
    {
        var byId = new Dictionary<string, GdAnswer>(StringComparer.Ordinal);
        foreach (var a in previous)
            byId.TryAdd(a.AnswerId ?? "", a);

        var pairs = new List<(GdV02Answer, GdAnswer?)>(current.Count);
        foreach (var a in current)
        {
            GdAnswer? prev = byId.GetValueOrDefault(a.AnswerId ?? "");
            pairs.Add((a, prev));
        }
        return pairs;
    }

    // ── (a) H→G — typed, per the answer type-compatibility policy ──────────────
    private static void MapAnswer(
        List<CellWriteEntry> cells,
        List<TaskMessage> messages,
        GdV02Question cq,
        GdV02Answer ca,
        GdAnswer pa,
        GdV02Config cfg,
        IReadOnlyDictionary<int, (string? DvType, object? Native)> sourceHByAnchorRow,
        IReadOnlyDictionary<int, string?> targetHByAnchorRow)
    {
        sourceHByAnchorRow.TryGetValue(pa.AnchorRow, out var src); // (null, null) when absent
        targetHByAnchorRow.TryGetValue(ca.AnchorRow, out var tgtCat);

        var srcCat = src.DvType;
        var native = src.Native;
        var g = cfg.PreviousAnswerColumn;

        // 1. blank source — checked FIRST, before any type logic.
        if (native is null || (native is string s && string.IsNullOrWhiteSpace(s)))
            return;

        var textFallback = pa.Answer ?? native.ToString() ?? "";

        // 2. equal category (string-equal, incl. both null / both List / both numeric).
        if (string.Equals(srcCat, tgtCat, StringComparison.Ordinal))
        {
            cells.Add(new CellWriteEntry(ca.AnchorRow, g, textFallback, native));
            return;
        }

        // 3. widen: WholeNumber → Decimal.
        if (IsWhole(srcCat) && IsDecimal(tgtCat))
        {
            cells.Add(new CellWriteEntry(ca.AnchorRow, g, textFallback, native));
            messages.Add(new(MessageSeverity.Warning,
                $"Question {Xref(cq)} answer {Aid(ca)} (row {ca.AnchorRow}): answer type widened " +
                $"(WholeNumber → Decimal) — value written as-is.",
                DateTimeOffset.Now));
            return;
        }

        // 4. narrow: Decimal → WholeNumber — written AS-IS (no rounding).
        if (IsDecimal(srcCat) && IsWhole(tgtCat))
        {
            cells.Add(new CellWriteEntry(ca.AnchorRow, g, textFallback, native));
            messages.Add(new(MessageSeverity.Warning,
                $"Question {Xref(cq)} answer {Aid(ca)} (row {ca.AnchorRow}): answer type narrowed " +
                $"(Decimal → WholeNumber) — value written as-is (not rounded).",
                DateTimeOffset.Now));
            return;
        }

        // 5. mismatch: any other pair — Error, skip the G cell, continue.
        messages.Add(new(MessageSeverity.Error,
            $"Question {Xref(cq)} answer {Aid(ca)} (row {ca.AnchorRow}): incompatible answer types " +
            $"(source {Cat(srcCat)} → target {Cat(tgtCat)}) — previous answer not injected.",
            DateTimeOffset.Now));
    }

    // ── (b) K→J — per explanation-row, position/index-aligned, text ────────────
    private static void MapExplanations(
        List<CellWriteEntry> cells,
        List<TaskMessage> messages,
        GdV02Question cq,
        GdV02Answer ca,
        GdAnswer pa,
        GdV02Config cfg)
    {
        var j = cfg.PreviousExplanationColumn;
        var n = Math.Min(pa.Explanations.Count, ca.Explanations.Count);

        for (var i = 0; i < n; i++)
        {
            var src = pa.Explanations[i].Current;          // v01 K value
            var targetRow = ca.Explanations[i].RowNumber;  // v02 row to write
            if (!string.IsNullOrWhiteSpace(src))
                cells.Add(new CellWriteEntry(targetRow, j, src!));
        }

        if (pa.Explanations.Count != ca.Explanations.Count)
            messages.Add(new(MessageSeverity.Warning,
                $"Question {Xref(cq)} answer {Aid(ca)}: explanation row count mismatch " +
                $"(source {pa.Explanations.Count}, target {ca.Explanations.Count}) — overlap written.",
                DateTimeOffset.Now));
    }

    // ── (c) O→P — text, once at the v02 answer anchor row ──────────────────────
    private static void MapProvidedBy(
        List<CellWriteEntry> cells,
        GdV02Answer ca,
        GdAnswer pa,
        GdV02Config cfg)
    {
        if (!string.IsNullOrWhiteSpace(pa.ProvidedBy))
            cells.Add(new CellWriteEntry(ca.AnchorRow, cfg.ProvidedByColumn, pa.ProvidedBy!));
    }

    private static bool IsWhole(string? cat) => cat == "WholeNumber";
    private static bool IsDecimal(string? cat) => cat == "Decimal";

    private static string Xref(GdV02Question c) => c.XrefId ?? "<none>";
    private static string Aid(GdV02Answer a) => a.AnswerId ?? "<bare>";
    private static string Cat(string? cat) => cat ?? "<none>";
}
