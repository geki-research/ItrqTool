namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

// ── MalformedKeyCheck<T> — structure check for blank or duplicate XrefId keys ──
//
// Generic extension that surfaces malformed XrefId keys across all three workbooks.
// Mirrors ClqBaselineChecks Phase 1 exactly: iterates alignment.MalformedKeys with
// no workbook filter, emits one finding per MalformedKey entry with id
// "structure.xrefid-empty-or-duplicated" (Fatal, Structure). For a duplicate XrefId
// on N rows, AlignmentEngine.ClassifyKeys produces N entries → N findings.
//
// The column is the XrefId column letter supplied at construction (config.XrefIdColumn
// for RLQ-v01); the check-result text and WorkbookName mapping are lifted verbatim
// from ClqBaselineChecks to guarantee byte-identical output for the same malformed keys.
// ClqBaselineChecks is NOT imported — parity is by reproduction, not shared code.

public sealed class MalformedKeyCheck<T> : IExtensionCheck<T>
    where T : class, IAlignmentIdentity
{
    private const string MalformedKeyId = "structure.xrefid-empty-or-duplicated";
    private readonly string _column;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public MalformedKeyCheck(string column, FindingEvaluation defaultEvaluation = FindingEvaluation.Fatal)
    {
        if (string.IsNullOrWhiteSpace(column))
            throw new ArgumentException("column must be non-empty.", nameof(column));
        _column = column;
        _descriptors = new[]
        {
            new FindingDescriptor(MalformedKeyId, defaultEvaluation, ValidationCheck.Structure,
                "The cross-reference identity key in a question row is blank or duplicated within its workbook, so the question cannot be reliably matched within or across years."),
        };
    }

    public IReadOnlyList<FindingDescriptor> Descriptors => _descriptors;

    public IReadOnlyList<ValidationFinding> Run(AlignmentResult<T> alignment, FindingEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(emitter);

        var findings = new List<ValidationFinding>();
        foreach (var mk in alignment.MalformedKeys)   // NO workbook filter — mirror CLQ Phase 1
        {
            var reason = mk.Reason == MalformedKeyReason.Blank
                ? "blank"
                : $"duplicated ('{mk.XrefId}')";
            findings.Add(emitter.Emit(MalformedKeyId,
                $"{_column}{mk.RowNumber}",
                questionNumber: null, questionText: null, requestedData: null, providedBy: null,
                $"Identity key in {WorkbookName(mk.Workbook)} at row {mk.RowNumber} is {reason}; " +
                "the question cannot be reliably matched within or across years."));
        }
        return findings;
    }

    private static string WorkbookName(ValidationWorkbook w) => w switch
    {
        ValidationWorkbook.CurrentResponse  => "the current response",
        ValidationWorkbook.EmptyTemplate    => "the empty template",
        ValidationWorkbook.PreviousResponse => "the previous response",
        _ => w.ToString()
    };
}
