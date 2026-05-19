namespace ItrqTool.Domain.Reporting;

public record HtmlDiffReportData(
    string Title,
    string PreviousWorkbookPath,
    string CurrentWorkbookPath,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<HtmlDiffQuestion>         Added,
    IReadOnlyList<HtmlDiffQuestion>         Removed,
    IReadOnlyList<HtmlDiffChangedQuestion>  Changed,
    IReadOnlyList<HtmlDiffValidationChange> ValidationChanges,
    IReadOnlyList<HtmlDiffQuestion>         Unchanged
);

public record HtmlDiffQuestion(
    string? QuestionNumber,
    string  Chapter,
    string  Section,
    string  QuestionText,
    string? DvType,
    string? CfOperator
);

public record HtmlDiffChangedQuestion(
    string  Chapter,
    string  Section,
    string? PreviousNumber,
    string? CurrentNumber,
    string  OldText,
    string  NewText,
    double  SimilarityScore,
    bool    DvTypeChanged,
    bool    CfOperatorChanged
);

public record HtmlDiffValidationChange(
    string? QuestionNumber,
    string  Chapter,
    string  Section,
    string  QuestionText,
    string? OldDvType,
    string? NewDvType,
    string? OldCfOperator,
    string? NewCfOperator
);
