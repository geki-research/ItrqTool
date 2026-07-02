using ItrqTool.Domain;
using ItrqTool.Tasks.ControlLevelQuestionValidationV01;
using ItrqTool.Tasks.ControlLevelQuestionValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.ControlLevelQuestionInject;

/// <summary>
/// Pure mapper from a CROSS-FORMAT alignment (current v01 ↔ previous v02) to the set of
/// <see cref="CellWriteEntry"/> the inject task writes into the v01 template, plus warning
/// messages for the ambiguous outcomes. No I/O, no Excel, no mutation of inputs.
///
/// Per <see cref="CrossYearOutcome"/>:
///   • Agree                     → inject (always reference columns F/G/M; carry-forward H/I/J when enabled).
///   • XrefIdConflict /
///     NewXrefIdWithLookalike /
///     SameXrefIdTextDiverged     → NO cells; ONE Warning naming the row + xref + outcome.
///   • Neither /
///     NotEvaluatedMalformedKey   → NO cells, no warning (genuine new / structural).
///
/// Cell emission obeys a non-blank-or-omit rule: a source value that is null/whitespace
/// leaves the target cell untouched (no <see cref="CellWriteEntry"/> emitted).
///
/// The H carry-forward write (BL-053 P4c-C2) is gated by <see cref="InjectionValueGuard.Evaluate"/>
/// against the target's FULL data-validation rule (type, operator, both formulas, resolved List
/// vocabulary), mirroring <c>RlqInjectMapper.MapAnswer</c>:
///   1. blank source (p.Answer null/whitespace) → omit the H cell (no message) — unchanged.
///   2. guard Inject → write p.Answer AS TODAY (untyped string write — CLQ never switches to a
///      typed write). If the source/target categories are WholeNumber→Decimal (a widen), ALSO
///      emit an Info note (value written as-is).
///   3. guard Skip → SKIP the H cell, emit the guard's SkipReason at its SkipSeverity (Warning or
///      Error), CONTINUE (task still succeeds; I/J carry-forward and F/G/M references still run).
/// I/J carry-forward and F/G/M reference writes carry no DV — not guarded.
/// </summary>
public static class ClqInjectMapper
{
    public static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) Map(
        CrossFormatAlignmentResult<ClqV01Question, ClqV02Question> alignment,
        ClqInjectConfig injectConfig,
        ClqV01Config currentConfig,
        // BL-053 P4c-C2: keyed by sourceHByRow (v02 previous RowNumber) / targetHByRow (v01
        // current RowNumber). Mirrors RlqInjectMapper.Map's sourceHByRow/targetHByRow parameters.
        IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)> sourceHByRow,
        IReadOnlyDictionary<int, ClqTargetDvHolder> targetHByRow)
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

                    // ── Reference-injection — ALWAYS on Agree ──
                    AddIfPresent(cells, c.RowNumber, currentConfig.PreviousAnswerColumn, p.Answer);

                    var explanation = BuildExplanation(p.Strengths, p.Weaknesses, injectConfig);
                    AddIfPresent(cells, c.RowNumber, currentConfig.PreviousExplanationColumn, explanation);

                    AddIfPresent(cells, c.RowNumber, currentConfig.ProvidedByColumn, p.ProvidedBy);

                    // ── Carry-forward — only when enabled AND stability == trigger token ──
                    if (injectConfig.CarryForwardEnabled &&
                        string.Equals(p.AnswerStability, injectConfig.StabilityTriggerToken, StringComparison.Ordinal))
                    {
                        MapCarryForwardAnswer(cells, messages, c, p, currentConfig, sourceHByRow, targetHByRow);
                        AddIfPresent(cells, c.RowNumber, currentConfig.StrengthsColumn, p.Strengths);
                        AddIfPresent(cells, c.RowNumber, currentConfig.WeaknessesColumn, p.Weaknesses);
                    }

                    break;
                }

                case CrossYearOutcome.XrefIdConflict:
                case CrossYearOutcome.NewXrefIdWithLookalike:
                case CrossYearOutcome.SameXrefIdTextDiverged:
                {
                    messages.Add(new(MessageSeverity.Warning,
                        $"Row {c.RowNumber} (xref {c.XrefId ?? "<none>"}): ambiguous previous match " +
                        $"({match.Outcome}) — left untouched.",
                        DateTimeOffset.Now));
                    break;
                }

                case CrossYearOutcome.Neither:
                case CrossYearOutcome.NotEvaluatedMalformedKey:
                    // Genuine new / structural — no cells, no warning.
                    break;
            }
        }

        return (cells, messages);
    }

    /// <summary>
    /// The signed-off D5 explanation rendering. The <c>{nl}</c> token in the config prefixes
    /// and separator is translated to a real newline. Returns the empty string when both
    /// strengths and weaknesses are blank (caller then omits the G cell).
    /// </summary>
    private static string BuildExplanation(string? strengths, string? weaknesses, ClqInjectConfig cfg)
    {
        static string Nl(string s) => s.Replace("{nl}", "\n");

        var segments = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(strengths))
            segments.Add(Nl(cfg.ExplanationStrengthsPrefix) + strengths);

        if (!string.IsNullOrWhiteSpace(weaknesses))
            segments.Add(Nl(cfg.ExplanationWeaknessesPrefix) + weaknesses);

        return string.Join(Nl(cfg.ExplanationMergeSeparator), segments);
    }

    private static void AddIfPresent(List<CellWriteEntry> cells, int rowNumber, string column, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            cells.Add(new CellWriteEntry(rowNumber, column, value));
    }

    // ── H carry-forward — gated by InjectionValueGuard against the target's DV rule ───────────
    private static void MapCarryForwardAnswer(
        List<CellWriteEntry> cells,
        List<TaskMessage> messages,
        ClqV01Question c,
        ClqV02Question p,
        ClqV01Config cfg,
        IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)> sourceHByRow,
        IReadOnlyDictionary<int, ClqTargetDvHolder> targetHByRow)
    {
        // 1. blank source — checked FIRST, before any guard logic. Unchanged from before.
        if (string.IsNullOrWhiteSpace(p.Answer))
            return;

        sourceHByRow.TryGetValue(p.RowNumber, out var src); // (null, null, null) when absent
        targetHByRow.TryGetValue(c.RowNumber, out var targetHolder);

        var decision = InjectionValueGuard.Evaluate(
            sourceText: p.Answer,
            sourceDvType: src.DvType,
            targetDvType: targetHolder?.Type,
            targetDvOperator: targetHolder?.Operator,
            targetDvFormula: targetHolder?.Formula,
            targetDvFormula2: targetHolder?.Formula2,
            targetResolvedListValues: targetHolder?.ListValues);

        if (decision.Decision == InjectionDecision.Inject)
        {
            AddIfPresent(cells, c.RowNumber, cfg.AnswerColumn, p.Answer);

            // Delta A: widen (WholeNumber → Decimal) is now conformant-and-injected — still
            // worth an informational note that the value was widened, not rounded/converted.
            if (IsWhole(src.DvType) && IsDecimal(targetHolder?.Type))
                messages.Add(new(MessageSeverity.Info,
                    $"Row {c.RowNumber} (xref {c.XrefId ?? "<none>"}): answer type widened (WholeNumber → Decimal) — value written as-is.",
                    DateTimeOffset.Now));
        }
        else
        {
            var severity = decision.SkipSeverity == SkipSeverity.Error
                ? MessageSeverity.Error
                : MessageSeverity.Warning;
            messages.Add(new(severity,
                $"Row {c.RowNumber} (xref {c.XrefId ?? "<none>"}): {decision.SkipReason}",
                DateTimeOffset.Now));
        }
    }

    private static bool IsWhole(string? cat) => cat == "WholeNumber";
    private static bool IsDecimal(string? cat) => cat == "Decimal";
}
