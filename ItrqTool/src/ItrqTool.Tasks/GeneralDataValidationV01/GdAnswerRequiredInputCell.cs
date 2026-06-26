using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

namespace ItrqTool.Tasks.GeneralDataValidationV01;

// ── GdAnswerRequiredInputCell — presence-only per-ANSWER required-input check ──
//
// The GD per-answer analogue of RequiredInputCellAnyValue<T> (which emits at question grain and
// cannot emit N findings per question). This check iterates answers directly and emits one
// finding per offending answer-cell at GdPerAnswerEmit.CellAddress, so a question with 3 answers
// can produce 0–3 findings.
//
// Used twice in the GD profile:
//   H (answer role) — gate = AllSections (every question's answers are checked).
//   L (material-change role) — gate = SectionsIn(config.MaterialChangeSections) so only
//     answers in the sections where L is required are checked; other sections skip.
//
// Finding id mirrors RequiredInputCellAnyValue: input-cell.{role}.missing (lesson 107).
// Each role must produce unique ids across the assembled catalogue.
//
// Gate: skips NotEvaluatedMalformedKey rows (the identity-integrity gate halts all dependent
// checks before they see malformed rows, but the skip is explicit for standalone unit-test safety).

public sealed class GdAnswerRequiredInputCell : IExtensionCheck<GdV01Question>
{
    private readonly Func<GdAnswer, string?> _value;
    private readonly Func<GdAnswer, string?> _providedBy;
    private readonly string _column;
    private readonly string _missingId;
    private readonly Func<GdV01Question, bool> _sectionGate;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public GdAnswerRequiredInputCell(
        Func<GdAnswer, string?> valueSelector,
        Func<GdAnswer, string?> providedBySelector,
        string role,
        string column,
        Func<GdV01Question, bool> sectionGate,
        FindingEvaluation missingDefault = FindingEvaluation.Error)
    {
        _value      = valueSelector      ?? throw new ArgumentNullException(nameof(valueSelector));
        _providedBy = providedBySelector ?? throw new ArgumentNullException(nameof(providedBySelector));
        if (string.IsNullOrWhiteSpace(role))   throw new ArgumentException("role must be non-empty.",   nameof(role));
        if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("column must be non-empty.", nameof(column));
        _column      = column;
        _sectionGate = sectionGate ?? throw new ArgumentNullException(nameof(sectionGate));
        _missingId   = $"input-cell.{role}.missing";
        _descriptors = new[]
        {
            new FindingDescriptor(_missingId, missingDefault, ValidationCheck.MissingResponse,
                "A required input cell is empty; the value was not provided."),
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
            if (aq.WithinYear == WithinYearJoin.NotEvaluatedMalformedKey)
                continue;

            var cur = aq.Current;
            if (!_sectionGate(cur))
                continue;

            foreach (var answer in cur.Answers)
            {
                if (string.IsNullOrWhiteSpace(_value(answer)))
                {
                    var cell = GdPerAnswerEmit.CellAddress(_column, answer);
                    findings.Add(emitter.Emit(_missingId,
                        cell, cur.QuestionNumber, cur.QuestionText,
                        requestedData: null, providedBy: _providedBy(answer),
                        $"Required input cell {cell} is empty; the value was not provided."));
                }
            }
        }
        return findings;
    }
}
