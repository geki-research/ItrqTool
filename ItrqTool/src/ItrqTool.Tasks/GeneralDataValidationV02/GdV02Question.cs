using ItrqTool.Tasks.QuestionnaireValidation.Alignment;

namespace ItrqTool.Tasks.GeneralDataValidationV02;

// GD_v02 question record. Structural twin of GdV01Question (same 6 IAlignmentIdentity members +
// payload nesting), differing only in the answer payload type: GdV02Answer carries the new
// HowExplanation (M) field. The identity surface (RowNumber/XrefId/OriginalText/QuestionText/
// SectionName/QuestionNumber) is unchanged by the +1 column shift — those fields come from
// C/D/R (XrefId), which are unshifted or carry the same identity value.
public sealed record GdV02Question(
    // IAlignmentIdentity (6)
    int RowNumber,            // qid anchor (min row across the qid)
    string? XrefId,           // the qid (substring before the first ':')
    string OriginalText,      // display-block column D text
    string QuestionText,      // = OriginalText (no number prefix to strip)
    string SectionName,       // from column D at the section-header row
    string? QuestionNumber,   // display-block column C
    // payload
    IReadOnlyList<GdV02Answer> Answers
) : IAlignmentIdentity;
