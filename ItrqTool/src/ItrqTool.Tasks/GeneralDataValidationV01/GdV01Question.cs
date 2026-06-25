using ItrqTool.Tasks.QuestionnaireValidation.Alignment;

namespace ItrqTool.Tasks.GeneralDataValidationV01;

// GD_v01 question record. The ALIGNMENT UNIT is the question-id (qid = the substring of the
// XrefId before the first ':'), so it implements the 6-field IAlignmentIdentity (same member
// names / types / nullability as the interface) plus the GD payload: a nested list of answers,
// each carrying its own once-per-answer cells and per-row explanation triplets.
//
// Identity mapping (recon §4.2):
//   - RowNumber       — the qid's ANCHOR row (min row across all the qid's rows);
//   - XrefId          — the qid (substring before the first ':' of the row XrefIds);
//   - OriginalText    — the display-block question text (column D), resolved from the
//                       most-recent non-blank D at/above the qid's first row (merged C/D blocks
//                       span the display question, which may cover MULTIPLE qids — recon §3);
//   - QuestionText    — == OriginalText (GD has no number prefix to strip — the number lives in
//                       its own column C);
//   - SectionName     — from column D at the section-header row;
//   - QuestionNumber  — the display-block question number (column C), resolved the same way as D.
//
// Differences from RLQ's single-answer record: an RLQ question has exactly one answer (the
// once-per-question H/G/L/O on the first row) plus a flat ExplanationRows list. A GD question
// has N answers, each with its own H/G/L/O and its own explanation rows — hence the
// Answers nesting rather than flat once-per-question fields.
public sealed record GdV01Question(
    // IAlignmentIdentity (6)
    int RowNumber,            // qid anchor (min row across the qid)
    string? XrefId,           // the qid (substring before the first ':')
    string OriginalText,      // display-block column D text
    string QuestionText,      // = OriginalText (no number prefix to strip)
    string SectionName,       // from column D at the section-header row
    string? QuestionNumber,   // display-block column C
    // payload
    IReadOnlyList<GdAnswer> Answers
) : IAlignmentIdentity;
