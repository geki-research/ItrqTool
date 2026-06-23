namespace ItrqTool.Tasks.RiskLevelQuestionValidationV02;

using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01; // RlqExplanationRow lives here (not cloned)

// v02-typed clone of ExplanationCompletenessCell (ItrqTool.Tasks.QuestionnaireValidation.Checks).
// The original implements IExtensionCheck<RlqV01Question> and cannot be placed in a
// ValidationPipelineProfile<RlqV02Question>. Logic and descriptor ids are byte-for-byte
// identical; only the generic type argument changes from RlqV01Question to RlqV02Question.
// RlqExplanationRow is shared — it lives in the v01 namespace and is referenced, not cloned.
internal sealed class ExplanationCompletenessCellV02 : IExtensionCheck<RlqV02Question>
{
    private readonly string _column;
    private readonly string _incompleteId;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public ExplanationCompletenessCellV02(
        string column,
        FindingEvaluation incompleteDefault = FindingEvaluation.Error)
    {
        if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("column must be non-empty.", nameof(column));
        _column = column;
        _incompleteId = "input-cell.explanation.incomplete";
        _descriptors = new[]
        {
            new FindingDescriptor(_incompleteId, incompleteDefault, ValidationCheck.MissingResponse,
                "An explanation was requested but the current explanation is missing."),
        };
    }

    public IReadOnlyList<FindingDescriptor> Descriptors => _descriptors;

    public IReadOnlyList<ValidationFinding> Run(AlignmentResult<RlqV02Question> alignment, FindingEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(emitter);

        var findings = new List<ValidationFinding>();
        foreach (var aq in alignment.Aligned)
        {
            if (aq.WithinYear == WithinYearJoin.NotEvaluatedMalformedKey)
                continue;

            var cur = aq.Current;
            foreach (var row in cur.ExplanationRows)
            {
                if (!string.IsNullOrWhiteSpace(row.Requested)
                    && string.IsNullOrWhiteSpace(row.Current))
                {
                    findings.Add(emitter.Emit(_incompleteId,
                        $"{_column}{row.RowNumber}", cur.QuestionNumber, cur.QuestionText,
                        requestedData: row.Requested, providedBy: cur.ProvidedBy,
                        $"An explanation was requested but the current explanation at " +
                        $"{_column}{row.RowNumber} is missing."));
                }
            }
        }
        return findings;
    }
}
