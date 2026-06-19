using ItrqTool.Domain;
using ItrqTool.Tasks.ControlLevelQuestionValidationV01;
using ItrqTool.Tasks.ControlLevelQuestionValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;

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
/// </summary>
public static class ClqInjectMapper
{
    public static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) Map(
        CrossFormatAlignmentResult<ClqV01Question, ClqV02Question> alignment,
        ClqInjectConfig injectConfig,
        ClqV01Config currentConfig)
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
                        AddIfPresent(cells, c.RowNumber, currentConfig.AnswerColumn, p.Answer);
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
}
