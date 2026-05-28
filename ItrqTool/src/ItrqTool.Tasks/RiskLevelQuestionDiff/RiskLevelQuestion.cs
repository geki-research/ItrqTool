namespace ItrqTool.Tasks.RiskLevelQuestionDiff;

public sealed record RiskLevelQuestion(
    string  SectionName,
    string  QuestionText,
    string  ExplanationPrompt,
    string? QuestionNumber,    // read directly from column B; null only on header/section rows
    int     RowNumber,
    string? DvType,
    string? DvFormula,
    string? CfOperator,
    string? DvOperator = null,
    string? DvFormula2 = null,
    string? CfType = null,
    string? CfValue = null,
    string? CfValue2 = null
);
