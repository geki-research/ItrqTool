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
    double? SecondBestSimilarity,
    string  OldDvDisplay,       // "—" if no DV
    string  NewDvDisplay,
    string? OldCfOperator,
    string? NewCfOperator,
    bool    TextChanged,
    bool    NumberChanged,
    bool    DvChanged,
    bool    CfChanged           // false when old DvType == "List" (presentational noise)
);

public record HtmlDiffUnchangedQuestion(
    string  Chapter,
    string  Section,
    string? QuestionNumber,
    string  QuestionText,
    string  DvDisplay,          // formatted: "—", type name, or "List: A | B | C"
    string? CfOperator,
    double  SimilarityScore,    // always 1.0
    double? SecondBestSimilarity
);

