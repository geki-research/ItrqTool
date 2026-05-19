namespace ItrqTool.Domain.Reporting;

public record HtmlDiffReportData(
    string PreviousWorkbookPath,
    string CurrentWorkbookPath,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<HtmlDiffQuestion>         Added,
    IReadOnlyList<HtmlDiffQuestion>         Removed,
    IReadOnlyList<HtmlDiffChangedQuestion>  Changed,
    IReadOnlyList<HtmlDiffValidationChange> ValidationChanges
);

public record HtmlDiffQuestion(
    string Chapter,
    string Section,
    string QuestionText,
    string? DvType,
    string? CfOperator
);

public record HtmlDiffChangedQuestion(
    string Chapter,
    string Section,
    string OldText,
    string NewText,
    double SimilarityScore,
    bool   DvTypeChanged,
    bool   CfOperatorChanged
);

public record HtmlDiffValidationChange(
    string  Chapter,
    string  Section,
    string  QuestionText,
    string? OldDvType,
    string? NewDvType,
    string? OldCfOperator,
    string? NewCfOperator
);
