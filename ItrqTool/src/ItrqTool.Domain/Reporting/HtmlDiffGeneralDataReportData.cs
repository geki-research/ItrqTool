namespace ItrqTool.Domain.Reporting;

/// <summary>
/// Report-data wrapper for General Data diff output. Parallel to (not extending)
/// HtmlDiffReportData; rendered by IHtmlGeneralDataDiffReportWriter.
/// </summary>
public record HtmlDiffGeneralDataReportData(
    string Title,
    string PreviousWorkbookPath,
    string CurrentWorkbookPath,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<HtmlDiffGeneralDataQuestion>           Added,
    IReadOnlyList<HtmlDiffGeneralDataQuestion>           Removed,
    IReadOnlyList<HtmlDiffGeneralDataChangedQuestion>    Changed,
    IReadOnlyList<HtmlDiffGeneralDataUnchangedQuestion>  Unchanged
);

/// <summary>
/// Question record for Added / Removed lists. Carries the full per-cell payload
/// so the writer can render template labels and DV/CF for each input cell.
/// </summary>
public record HtmlDiffGeneralDataQuestion(
    string? QuestionNumber,                          // first row's column B
    string  Section,
    int     RowNumber,                               // first row's sheet row number
    string  QuestionText,
    IReadOnlyList<string> RowNumberLabels,           // per-row column B; length = rowspan
    IReadOnlyList<HtmlDiffGeneralDataAnswerCell> AnswerCells,
    IReadOnlyList<HtmlDiffGeneralDataExplanationCell> ExplanationCells
);

public record HtmlDiffGeneralDataAnswerCell(
    int     RowOffset,
    string  Column,                                  // "D" | "E" | "F"
    string  Text,
    string  DvDisplay,                               // formatted: "—" | Excel-words rule | "List: A | B | C"
    string  CfDisplay                                // formatted CF display; "—" if no CF
);

public record HtmlDiffGeneralDataExplanationCell(
    int     RowOffset,
    string  Text,
    string  DvDisplay,
    string  CfDisplay
);

/// <summary>
/// Question record for the Changed list. Carries per-cell diff payload alongside
/// question-level diff flags.
/// </summary>
public record HtmlDiffGeneralDataChangedQuestion(
    string  Section,
    int     PreviousRowNumber,
    int     CurrentRowNumber,
    string? PreviousNumber,
    string? CurrentNumber,
    string  OldText,
    string  NewText,
    double  SimilarityScore,
    double? SecondBestSimilarity,
    bool    TextChanged,                             // base similarity < 1.0 on QuestionText
    bool    NumberChanged,
    bool    AnswerCellsChanged,                      // AnswerCellChanges is non-empty
    bool    ExplanationCellsChanged,                 // ExplanationCellChanges is non-empty
    IReadOnlyList<HtmlDiffAnswerCellChange>      AnswerCellChanges,
    IReadOnlyList<HtmlDiffExplanationCellChange> ExplanationCellChanges
);

/// <summary>
/// Per-cell diff entry for AnswerCells (D/E/F). OldText is null when the cell was
/// added in the new year; NewText is null when removed in the new year.
/// </summary>
public record HtmlDiffAnswerCellChange(
    int     RowOffset,
    string  Column,
    string? OldText,
    string? NewText,
    string  OldDvDisplay,                            // "—" when no DV on old cell
    string  NewDvDisplay,
    string  OldCfDisplay,                            // formatted CF display; "—" if no CF
    string  NewCfDisplay,
    bool    TextChanged,                             // (OldText ?? "") != (NewText ?? "")
    bool    DvChanged,                               // full DV comparison (type/operator/both values)
    bool    CfChanged                                // full CF comparison (type/operator/both values); no muting
);

/// <summary>
/// Per-cell diff entry for ExplanationCells (column G). Same semantics as
/// HtmlDiffAnswerCellChange, no Column field (always G).
/// </summary>
public record HtmlDiffExplanationCellChange(
    int     RowOffset,
    string? OldText,
    string? NewText,
    string  OldDvDisplay,
    string  NewDvDisplay,
    string  OldCfDisplay,
    string  NewCfDisplay,
    bool    TextChanged,
    bool    DvChanged,
    bool    CfChanged
);

/// <summary>
/// Question record for Unchanged list. Carries the full payload (same as
/// HtmlDiffGeneralDataQuestion) for consistent rendering; SimilarityScore is
/// always 1.0 for unchanged matches.
/// </summary>
public record HtmlDiffGeneralDataUnchangedQuestion(
    string  Section,
    int     PreviousRowNumber,
    int     CurrentRowNumber,
    string? QuestionNumber,
    string  QuestionText,
    IReadOnlyList<string> RowNumberLabels,
    IReadOnlyList<HtmlDiffGeneralDataAnswerCell> AnswerCells,
    IReadOnlyList<HtmlDiffGeneralDataExplanationCell> ExplanationCells,
    double  SimilarityScore,                         // always 1.0
    double? SecondBestSimilarity
);
