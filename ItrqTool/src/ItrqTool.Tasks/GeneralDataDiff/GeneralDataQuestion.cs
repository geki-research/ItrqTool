namespace ItrqTool.Tasks.GeneralDataDiff;

/// <summary>
/// One template-label cell from columns D, E, or F of a General Data question.
/// </summary>
public sealed record GeneralDataAnswerCell(
    int     RowOffset,    // 0-based; row 0 = the question's first row
    string  Column,       // "D" | "E" | "F" (whichever AnswerColumns the config uses)
    string  Text,         // template label, e.g. "<# of Group FTEs>"
    string? DvType,
    string? DvFormula,
    string? CfOperator,
    string? DvOperator = null,
    string? DvFormula2 = null,
    string? CfType = null,
    string? CfValue = null,
    string? CfValue2 = null
);

/// <summary>
/// One explanation-prompt cell from column G of a General Data question.
/// Separate from GeneralDataAnswerCell because G is treated as a different field type
/// in the diff/rendering layer (prompt text vs answer-template label).
/// </summary>
public sealed record GeneralDataExplanationCell(
    int     RowOffset,
    string  Text,
    string? DvType,
    string? DvFormula,
    string? CfOperator,
    string? DvOperator = null,
    string? DvFormula2 = null,
    string? CfType = null,
    string? CfValue = null,
    string? CfValue2 = null
);

/// <summary>
/// One logical General Data question, potentially spanning multiple sheet rows.
/// </summary>
public sealed record GeneralDataQuestion(
    string  SectionName,
    string  QuestionText,                          // from column C of the question's first row
    string? QuestionNumber,                        // first row's column B; null if empty
    int     RowNumber,                             // first row's sheet row number
    IReadOnlyList<string> RowNumberLabels,         // per-row column B values, length = RowSpan
    IReadOnlyList<GeneralDataAnswerCell> AnswerCells,
    IReadOnlyList<GeneralDataExplanationCell> ExplanationCells
);
