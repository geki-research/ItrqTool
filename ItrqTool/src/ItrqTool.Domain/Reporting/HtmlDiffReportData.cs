namespace ItrqTool.Domain.Reporting;

public record HtmlDiffReportData(
    string Title,
    string PreviousWorkbookPath,
    string CurrentWorkbookPath,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<HtmlDiffQuestion>           Added,
    IReadOnlyList<HtmlDiffQuestion>           Removed,
    IReadOnlyList<HtmlDiffChangedQuestion>    Changed,
    IReadOnlyList<HtmlDiffUnchangedQuestion>  Unchanged
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
    string  OldDvDisplay,
    string  NewDvDisplay,
    string? OldCfOperator,
    string? NewCfOperator,
    bool    TextChanged,
    bool    NumberChanged,
    bool    DvChanged,
    bool    CfChanged
);

public record HtmlDiffUnchangedQuestion(
    string  Chapter,
    string  Section,
    string? QuestionNumber,
    string  QuestionText,
    string  DvDisplay,
    string? CfOperator
);
