using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

namespace ItrqTool.Tasks.GeneralDataValidationV01;

// ── GdAnswerTextDivergedCell — cross-year text-diverged signal at QID grain ──
//
// Surfaces the SameXrefIdTextDiverged cross-year outcome: the identity key matches a previous-year
// question but the question text has diverged beyond the match threshold — possibly a heavy
// rewrite or a reused key that should have been retired. Fires once per question (not per answer).
//
// Finding id + Check reused from ClqBaselineFindings (lesson 107):
//   cross-year.same-xrefid-text-diverged — ValidationCheck.Structure, Warning default.
//
// Emit at {column}{aq.Current.RowNumber} (the XrefId column at the question's anchor row).
// providedBySelector: typically q => q.Answers.FirstOrDefault()?.ProvidedBy (wired in C2b).

public sealed class GdAnswerTextDivergedCell : IExtensionCheck<GdV01Question>
{
    private readonly Func<GdV01Question, string?> _providedBy;
    private readonly string _column;
    private readonly string _divergedId;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public GdAnswerTextDivergedCell(
        Func<GdV01Question, string?> providedBySelector,
        string column,
        FindingEvaluation divergedDefault = FindingEvaluation.Warning)
    {
        _providedBy = providedBySelector ?? throw new ArgumentNullException(nameof(providedBySelector));
        if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("column must be non-empty.", nameof(column));
        _column     = column;
        _divergedId = "cross-year.same-xrefid-text-diverged";
        _descriptors = new[]
        {
            new FindingDescriptor(_divergedId, divergedDefault, ValidationCheck.Structure,
                "The question shares an identity key with a previous-year question but its text has diverged beyond the match threshold — possibly a heavy rewrite or a reused key that should have been retired."),
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
            if (aq.CrossYear != CrossYearOutcome.SameXrefIdTextDiverged)
                continue;

            var cur  = aq.Current;
            int row  = cur.RowNumber;
            var prev = aq.XrefIdCounterpart;   // the previous question sharing this XrefId
            var prevDesc = prev is not null
                ? $"row {prev.RowNumber} ('{prev.XrefId}')"
                : "a previous question";

            findings.Add(emitter.Emit(_divergedId,
                $"{_column}{row}", cur.QuestionNumber, cur.QuestionText,
                requestedData: null, providedBy: _providedBy(cur),
                $"Identity key '{cur.XrefId}' is shared with previous {prevDesc} but the " +
                "question text has diverged beyond the match threshold. Verify whether this is a heavy rewrite " +
                "or a reused key."));
        }
        return findings;
    }
}
