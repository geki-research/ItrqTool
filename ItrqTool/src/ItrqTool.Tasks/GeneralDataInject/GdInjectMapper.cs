using ItrqTool.Domain;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.GeneralDataValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.Shared;

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
/// The H→G decision (BL-053 P4b-G2) is delegated to <see cref="InjectionValueGuard.Evaluate"/>,
/// keyed on the source cell's TextValue (the DV-governed literal — never a re-stringified
/// NativeValue) against the target's FULL data-validation rule (type, operator, both formulas,
/// resolved List vocabulary):
///   1. blank source (native null, or native a blank string) → omit the G cell (no message) —
///      checked BEFORE the guard, unchanged from before.
///   2. guard Inject → write the native value. If the source/target categories are
///      WholeNumber→Decimal (a widen), ALSO emit an Info note (value written as-is).
///   3. guard Skip → SKIP the G cell, emit the guard's SkipReason at its SkipSeverity
///      (Warning or Error), CONTINUE (task still succeeds; K→J / O→P still run).
/// The native value (native CLR value) is the locale-safe payload; its CLR type selects the
/// writer branch (Chunk A). The K→J and O→P writes are plain text — never typed.
/// The DV lookups are keyed by ANSWER AnchorRow (source by v01 anchor, target by v02 anchor).
/// </summary>
public static class GdInjectMapper
{
    public static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) Map(
        CrossFormatAlignmentResult<GdV02Question, GdV01Question> alignment,
        GdV02Config currentConfig,
        IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)> sourceHByAnchorRow, // v01 answer AnchorRow → H DV + native + text
        IReadOnlyDictionary<int, TargetDvInfo> targetHByAnchorRow)                 // v02 answer AnchorRow → H full DV rule
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

                    // BLG-0077: an Agree reached over a CHANGED question text is announced exactly
                    // once, before any cell work, at the same per-question grain as the arms below.
                    // Carrying a previous year's values into a re-worded question without saying so
                    // is precisely the silent acceptance this tool exists to prevent.
                    if (!GdInjectTextComparison.AreIdentical(cq.OriginalText, pq.OriginalText))
                    {
                        // BLG-0079: excerpts are windowed around the drift, computed from BOTH
                        // texts together, so a late-in-the-string change is visible rather than
                        // truncated away identically on both sides.
                        var (curEx, prevEx) =
                            DriftMessageFormatter.Excerpts(cq.OriginalText, pq.OriginalText);

                        messages.Add(new(MessageSeverity.Warning,
                            $"Question {Xref(cq)} (row {cq.RowNumber}): question text changed since " +
                            $"the previous year (similarity {Score(match.MatcherBaseScore)}) — treated " +
                            $"as the same question; previous values carried forward. " +
                            $"Previous (row {pq.RowNumber}): \"{prevEx}\". " +
                            $"Current: \"{curEx}\".",
                            DateTimeOffset.Now));
                    }

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

                // Split from the arm below by BLG-0077. This one is no longer "ambiguous": the qid
                // named a counterpart and the texts were COMPARED and fell short, so the operator is
                // told by how much and against what — the same evidence the Agree Warning carries,
                // so both sides of the threshold are equally diagnosable. Still no cells written.
                case CrossYearOutcome.SameXrefIdTextDiverged:
                {
                    var xq = match.XrefIdCounterpart;
                    var (curEx, prevEx) =
                        DriftMessageFormatter.Excerpts(cq.OriginalText, xq?.OriginalText);
                    var prevPart = xq is null
                        ? ""
                        : $" Previous (row {xq.RowNumber}): \"{prevEx}\".";

                    messages.Add(new(MessageSeverity.Warning,
                        $"Question {Xref(cq)} (row {cq.RowNumber}): question text changed since the " +
                        $"previous year and fell below the similarity threshold " +
                        $"(similarity {Score(match.MatcherBaseScore)}) — left untouched." +
                        $"{prevPart} Current: \"{curEx}\".",
                        DateTimeOffset.Now));
                    break;
                }

                // UNCHANGED by BLG-0077, message text included. These two mean the key and the text
                // disagree about identity — a different failure from a drifted wording — and neither
                // is reachable from GdInjectAligner's pure qid-join in any case.
                case CrossYearOutcome.XrefIdConflict:
                case CrossYearOutcome.NewXrefIdWithLookalike:
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

    // ── (a) H→G — typed, gated by InjectionValueGuard against the target's DV rule ─────────────
    private static void MapAnswer(
        List<CellWriteEntry> cells,
        List<TaskMessage> messages,
        GdV02Question cq,
        GdV02Answer ca,
        GdAnswer pa,
        GdV02Config cfg,
        IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)> sourceHByAnchorRow,
        IReadOnlyDictionary<int, TargetDvInfo> targetHByAnchorRow)
    {
        sourceHByAnchorRow.TryGetValue(pa.AnchorRow, out var src); // (null, null, null) when absent
        targetHByAnchorRow.TryGetValue(ca.AnchorRow, out var targetHolder);

        var srcCat = src.DvType;
        var native = src.Native;
        var tgtCat = targetHolder?.Type;
        var g = cfg.PreviousAnswerColumn;

        // 1. blank source — checked FIRST, before any guard logic. Unchanged from before.
        if (native is null || (native is string s && string.IsNullOrWhiteSpace(s)))
            return;

        var textFallback = pa.Answer ?? native.ToString() ?? "";

        // Invariant: a non-blank native value is always paired with a non-null TextValue —
        // both are read from the same source cell in BuildSourceHLookup. Never fall back to a
        // re-stringified Native.
        var decision = InjectionValueGuard.Evaluate(
            sourceText: src.TextValue!,
            sourceDvType: srcCat,
            targetDvType: targetHolder?.Type,
            targetDvOperator: targetHolder?.Operator,
            targetDvFormula: targetHolder?.Formula,
            targetDvFormula2: targetHolder?.Formula2,
            targetResolvedListValues: targetHolder?.ListValues,
            sourceNative: native);

        if (decision.Decision == InjectionDecision.Inject)
        {
            cells.Add(new CellWriteEntry(ca.AnchorRow, g, textFallback, native));

            // Delta A: widen (WholeNumber → Decimal) is now conformant-and-injected — still
            // worth an informational note that the value was widened, not rounded/converted.
            if (IsWhole(srcCat) && IsDecimal(tgtCat))
                messages.Add(new(MessageSeverity.Info,
                    $"Question {Xref(cq)} answer {Aid(ca)} (row {ca.AnchorRow}): answer type widened " +
                    $"(WholeNumber → Decimal) — value written as-is.",
                    DateTimeOffset.Now));
        }
        else
        {
            var severity = decision.SkipSeverity == SkipSeverity.Error
                ? MessageSeverity.Error
                : MessageSeverity.Warning;
            messages.Add(new(severity,
                $"Question {Xref(cq)} answer {Aid(ca)} (row {ca.AnchorRow}): {decision.SkipReason}",
                DateTimeOffset.Now));
        }
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

    // ── message formatting ────────────────────────────────────────────────────
    // BLG-0079 moved these to the shared DriftMessageFormatter (ItrqTool.Tasks.Shared), so all
    // three inject mappers render a drifted question identically, and lifted the excerpt from
    // head-truncation to a window centred on the drift. Score() is kept as a one-line local alias
    // so the call sites below read unchanged.
    private static string Score(double? score) => DriftMessageFormatter.FormatScore(score);
}
