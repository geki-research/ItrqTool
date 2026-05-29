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
    int     RowNumber,          // row in the sheet the question came from
    string  QuestionText,
    string  DvDisplay,          // formatted: "—", Excel-words rule, or "List: A | B | C"
    string  CfDisplay           // formatted: "—", "equal to X", "Formula: …", or type name
);

public record HtmlDiffChangedQuestion(
    string  Chapter,
    string  Section,
    int     PreviousRowNumber,
    int     CurrentRowNumber,
    string? PreviousNumber,
    string? CurrentNumber,
    string  OldText,
    string  NewText,
    double  SimilarityScore,
    double? SecondBestSimilarity,
    string  OldDvDisplay,       // "—" if no DV
    string  NewDvDisplay,
    string  OldCfDisplay,       // formatted CF display; "—" if no CF
    string  NewCfDisplay,
    bool    TextChanged,
    bool    NumberChanged,
    bool    DvChanged,
    bool    CfChanged,          // false when old DvType == "List" (presentational noise)
    string? OldExplanation     = null,
    string? NewExplanation     = null,
    bool    ExplanationChanged = false
);

public record HtmlDiffUnchangedQuestion(
    string  Chapter,
    string  Section,
    int     PreviousRowNumber,
    int     CurrentRowNumber,
    string? QuestionNumber,
    string  QuestionText,
    string  DvDisplay,          // formatted: "—", Excel-words rule, or "List: A | B | C"
    string  CfDisplay,          // formatted CF display; "—" if no CF
    double  SimilarityScore,    // always 1.0
    double? SecondBestSimilarity
);

