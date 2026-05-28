---
name: clq-diff
description: Control Level Questions sheet diff task (ControlLevelQuestionDiffTask, TaskType "ControlLevelQuestionDiff"). The CLQ config-file format (ControlLevelQuestionsConfig, sectionRows "<sectionRow>:<first>-<last>" format, example JSON), the AuditQuestion record semantics (prefix-strip / ExtractNumber), parser specifics, the DiffResult / ChangedQuestion / UnchangedQuestion record shapes, the five task parameters, and the report shape (shares HtmlQuestionDiffReportWriter with RLQ). Load when working on the Control Level Questions diff, its config, parser, or records. See also diff-task-conventions for the shared matching/DV/CF machinery.
---

# CLQ — Control Level Questions diff

`ControlLevelQuestionDiffTask` (TaskType `"ControlLevelQuestionDiff"`) compares the
Control Level Questions sheet between two reference years and produces an
interactive HTML diff report. Records and config live in namespace
`ItrqTool.Tasks.ControlLevelQuestionDiff`. Shared matching/DV/CF machinery is in
the `diff-task-conventions` skill.

## Parameters

Four task parameters:

| Parameter | Description |
|---|---|
| `previousWorkbookFullFilename` | Absolute path to the previous-year auditor-questionnaire workbook |
| `currentWorkbookFullFilename` | Absolute path to the current-year auditor-questionnaire workbook |
| `previousConfigurationFullFilename` | Absolute path to the CLQ config JSON for the previous workbook |
| `currentConfigurationFullFilename` | Absolute path to the CLQ config JSON for the current workbook |

Each config file is deserialized independently and applied only to its own
workbook, allowing the two workbooks to have different sheet structures (e.g.
across audit years). The config describes the structure of the "Control Level
Questions" sheet in a workbook.

## AuditQuestion

```csharp
public sealed record AuditQuestion(
    string  ChapterName,
    string  SectionName,
    string  QuestionText,      // prefix stripped
    string  OriginalText,      // raw cell text
    string? QuestionNumber,    // e.g. "1.2", null if no prefix present
    int     RowNumber,
    string? DvType,
    string? DvFormula,         // raw DV formula from ExcelCellStructure.DataValidationFormula
    string? CfOperator
);
// AuditQuestion.ExtractNumber("1.2) text") → "1.2"; no prefix → null
// AuditQuestion.StripPrefix("1.2) text")   → "text"
```

The question number is parsed out of the cell text prefix: `ExtractNumber`
returns the leading number token (`"1.2"`) or null when there is no prefix;
`StripPrefix` returns the text with that prefix removed. `QuestionText` holds the
stripped text; `OriginalText` holds the raw cell content.

## DiffResult / ChangedQuestion / UnchangedQuestion

A matched question pair is either Changed or Unchanged — there is **no** separate
ValidationChange category. CF changes are ignored when the old DvType is `"List"`
(presentational noise on dropdowns — see `diff-task-conventions`).

```csharp
public sealed record ChangedQuestion(
    AuditQuestion OldQuestion,
    AuditQuestion NewQuestion,
    double  SimilarityScore,
    double? SecondBestSimilarity,
    bool    TextChanged,        // SimilarityScore < 1.0
    bool    NumberChanged,
    bool    DvChanged,
    bool    CfChanged           // false when old DvType == "List"
);

public sealed record UnchangedQuestion(
    AuditQuestion Question,
    double? SecondBestSimilarity
);

public sealed record DiffResult(
    IReadOnlyList<AddedQuestion>      Added,
    IReadOnlyList<RemovedQuestion>    Removed,
    IReadOnlyList<ChangedQuestion>    Changed,
    IReadOnlyList<UnchangedQuestion>  Unchanged
);
```

## ControlLevelQuestionsConfig

```csharp
public sealed record SectionDefinition(int SectionRow, int FirstQuestionRow, int LastQuestionRow);

public sealed class ControlLevelQuestionsConfig
{
    public string SheetName { get; init; } = "Control Level Questions";
    public string TextColumn { get; init; } = "C";
    public string InputColumn { get; init; } = "D";
    public IReadOnlyList<int> ChapterRows { get; init; } = [];
    // Each entry: "<sectionRow>:<firstQuestionRow>-<lastQuestionRow>"
    public IReadOnlyList<string> SectionRows { get; init; } = [];
    // ParsedSections parses SectionRows; throws FormatException on invalid entries
    public IReadOnlyList<SectionDefinition> ParsedSections { get; }
}
```

### SectionRows format

Each string is `"<sectionRow>:<firstQuestionRow>-<lastQuestionRow>"`. All numbers
are 1-based Excel row numbers. Constraints:

- `sectionRow` must be a positive integer.
- `firstQuestionRow` must be greater than `sectionRow`.
- `lastQuestionRow` must be ≥ `firstQuestionRow`.

Rows not covered by any section range or chapter row are silently skipped during
parsing. `ParsedSections` throws `FormatException` on the first invalid entry;
`ControlLevelQuestionDiffTask` catches it and returns `Succeeded: false` with the
error message.

### Example CLQ config file

```json
{
  "sheetName": "Control Level Questions",
  "textColumn": "C",
  "inputColumn": "D",
  "chapterRows": [1, 15, 30],
  "sectionRows": ["2:3-14", "16:17-29", "31:32-50"]
}
```

## Report shape

CLQ shares `HtmlQuestionDiffReportWriter` with RLQ (same report shape and tabs,
including the sheet-order "Current sheet" / "Previous sheet" tabs described in
`diff-task-conventions`). The CLQ report has no explanation diff block (that is
RLQ-specific).
