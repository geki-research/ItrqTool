using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.RiskLevelQuestionInjectED;

/// <summary>
/// Pure mapper from a CROSS-FORMAT alignment (current ED v02 ↔ previous ED v01) to the set of
/// <see cref="CellWriteEntry"/> the ED RLQ inject task writes into the ED v02 template, plus
/// messages. A fork of <c>RlqInjectMapper</c>; no I/O, no Excel, no mutation of inputs.
/// Reference-only — there is no carry-forward.
///
/// Per <see cref="CrossYearOutcome"/>, on Agree it runs, in order:
///   • the drift check (standard text, unchanged);
///   • three independent answer arms (<see cref="RlqEDAnswerArm"/>) — each a clone of the
///     standard H→G arm, gated by <see cref="InjectionValueGuard"/>:
///       Answer  → <see cref="RlqV02Config.PreviousAnswerColumn"/>,
///       Holding → <see cref="RlqEDInjectConfig.TargetHoldingAnswerColumn"/>,
///       EBOe    → <see cref="RlqEDInjectConfig.TargetEBOeAnswerColumn"/>;
///     every message an arm emits is the standard text prefixed with "[label] ", and a skip in
///     one arm never prevents another;
///   • the standard K→J explanation write (per-row, position-aligned, text);
///   • the standard O→P provided-by write (text, once at the anchor row).
/// The ambiguous outcomes (XrefIdConflict / NewXrefIdWithLookalike / SameXrefIdTextDiverged)
/// emit ONE Warning and no cells; Neither / NotEvaluatedMalformedKey emit nothing.
/// </summary>
public static class RlqEDInjectMapper
{
    private static readonly RlqEDAnswerArm[] Arms =
        [RlqEDAnswerArm.Answer, RlqEDAnswerArm.Holding, RlqEDAnswerArm.EBOe];

    public static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) Map(
        CrossFormatAlignmentResult<RlqV02Question, RlqV01Question> alignment,
        RlqV02Config currentConfig,
        RlqEDInjectConfig edConfig,
        IReadOnlyDictionary<RlqEDAnswerArm, IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)>> sourceByArm, // inner key: v01 source RowNumber
        IReadOnlyDictionary<RlqEDAnswerArm, IReadOnlyDictionary<int, TargetDvInfo>> targetByArm)                // inner key: v02 current RowNumber
    {
        foreach (var arm in Arms)
        {
            if (!sourceByArm.ContainsKey(arm))
                throw new ArgumentException($"sourceByArm is missing the '{arm}' arm.", nameof(sourceByArm));
            if (!targetByArm.ContainsKey(arm))
                throw new ArgumentException($"targetByArm is missing the '{arm}' arm.", nameof(targetByArm));
        }

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

                    WarnIfTextDrifted(messages, c, p, match.MatcherBaseScore);

                    foreach (var arm in Arms)
                        MapAnswerArm(cells, messages, c, p, arm,
                            TargetColumn(arm, currentConfig, edConfig),
                            sourceByArm[arm], targetByArm[arm]);

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

    private static string TargetColumn(RlqEDAnswerArm arm, RlqV02Config cfg, RlqEDInjectConfig ed) => arm switch
    {
        RlqEDAnswerArm.Answer  => cfg.PreviousAnswerColumn,
        RlqEDAnswerArm.Holding => ed.TargetHoldingAnswerColumn,
        RlqEDAnswerArm.EBOe    => ed.TargetEBOeAnswerColumn,
        _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, null),
    };

    private static string Label(RlqEDAnswerArm arm) => arm switch
    {
        RlqEDAnswerArm.Answer  => "answer",
        RlqEDAnswerArm.Holding => "answer (Holding)",
        RlqEDAnswerArm.EBOe    => "answer (EBOe)",
        _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, null),
    };

    // ── (a) answer arms — typed, gated by InjectionValueGuard against the target's DV rule ─────
    //
    // A clone of the standard H→G arm, parameterised by arm. The only differences: the target
    // column is the arm's, every message carries the arm's "[label] " prefix, and the text
    // fallback for Holding / EBOe is the source cell's own TextValue (RlqV01Question carries no
    // Holding / EBOe text, so the standard p.Answer would be the TOTAL's text).
    private static void MapAnswerArm(
        List<CellWriteEntry> cells,
        List<TaskMessage> messages,
        RlqV02Question c,
        RlqV01Question p,
        RlqEDAnswerArm arm,
        string column,
        IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)> sourceByRow,
        IReadOnlyDictionary<int, TargetDvInfo> targetByRow)
    {
        sourceByRow.TryGetValue(p.RowNumber, out var src); // (null, null, null) when absent
        targetByRow.TryGetValue(c.RowNumber, out var targetHolder);

        var srcCat = src.DvType;
        var native = src.Native;
        var tgtCat = targetHolder?.Type;
        var prefix = "[" + Label(arm) + "] ";

        // 1. blank source — checked FIRST, before any guard logic.
        if (native is null || (native is string s && string.IsNullOrWhiteSpace(s)))
            return;

        var textFallback = (arm == RlqEDAnswerArm.Answer ? p.Answer : src.TextValue)
                           ?? native.ToString() ?? "";

        // Invariant: a non-blank native value is always paired with a non-null TextValue — both
        // are read from the same source cell. Never fall back to a re-stringified Native.
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
            cells.Add(new CellWriteEntry(c.RowNumber, column, textFallback, native));

            // Widen (WholeNumber → Decimal) is conformant-and-injected — still worth an
            // informational note that the value was widened, not rounded/converted.
            if (IsWhole(srcCat) && IsDecimal(tgtCat))
                messages.Add(new(MessageSeverity.Info,
                    prefix + $"Row {c.RowNumber} (xref {Xref(c)}): answer type widened (WholeNumber → Decimal) — value written as-is.",
                    DateTimeOffset.Now));
        }
        else
        {
            var severity = decision.SkipSeverity == SkipSeverity.Error
                ? MessageSeverity.Error
                : MessageSeverity.Warning;
            messages.Add(new(severity,
                prefix + $"Row {c.RowNumber} (xref {Xref(c)}): {decision.SkipReason}",
                DateTimeOffset.Now));
        }
    }

    // ── (b) K→J — per-row, position/index-aligned, text (standard, unchanged) ─────────────────
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
            var src = p.ExplanationRows[i].Current;       // v01 current-explanation value
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

    // ── (c) O→P — text, once at the anchor row (standard, unchanged) ──────────────────────────
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

    // ── non-identical Agree → exactly one Warning (standard BLG-0079 text, unchanged) ─────────
    // Emitted once per QUESTION, before any arm, so it cannot multiply per arm, row or cell.
    private static void WarnIfTextDrifted(
        List<TaskMessage> messages,
        RlqV02Question c,
        RlqV01Question p,
        double? baseScore)
    {
        if (DriftMessageFormatter.AreIdentical(c.OriginalText, p.OriginalText))
            return;

        var (curEx, prevEx) = DriftMessageFormatter.Excerpts(c.OriginalText, p.OriginalText);

        messages.Add(new(MessageSeverity.Warning,
            $"Row {c.RowNumber} (xref {Xref(c)}): question text changed since the previous year " +
            $"(similarity {DriftMessageFormatter.FormatScore(baseScore)}) — treated as the same " +
            $"question; previous values carried forward. " +
            $"Previous (row {p.RowNumber}): \"{prevEx}\". Current: \"{curEx}\".",
            DateTimeOffset.Now));
    }

    private static string Xref(RlqV02Question c) => c.XrefId ?? "<none>";
}
