---
name: gd-diff
description: General Data sheet diff task (GeneralDataDiffTask, TaskType "GeneralDataDiff"). The multi-row question model (a question spans multiple sheet rows with D/E/F answer cells and a G explanation cell), the structure-JSON format including the optional-rowspan shorthand, GeneralDataConfig / GeneralDataQuestionParser, the cell-inclusion rule, per-cell diffing (answer cells keyed by (RowOffset, Column), explanation cells by RowOffset), the full record family, the writer note (own parallel writer, duplicate-and-defer scaffold), the architectural note about GD exposing a standalone parser (the "third-sibling trigger did not fire" rationale), and the parameters. Load when working on the General Data diff, its config, parser, engine, per-cell diffing, or records. See also diff-task-conventions for the shared matching/DV/CF machinery.
---

# General Data diff

The third diff task. Compares the General Data sheet between two reference years;
produces an interactive HTML report. Unlike CLQ and RLQ (one question per sheet
row), a General Data question can span multiple sheet rows with a variable number
of template-label cells across columns D/E/F per row and an explanation cell in
column G. Shared matching/DV/CF machinery is in the `diff-task-conventions` skill.

- **Default sheet name**: `"General Data"`
- **Default columns**: B=question number, C=text/section header, D/E/F=answer
  template labels, G=explanation prompt
- **Task type**: `GeneralDataDiff`
- **Namespace**: `ItrqTool.Tasks.GeneralDataDiff` (engine, records, parser,
  formatter) and `ItrqTool.Tasks` (task orchestrator `GeneralDataDiffTask`)
- **Default report title**: `"General Data Diff Report"`
- **Default output filename**: `general-data-diff.html`
- **Matching**: question-text-based with section (+0.10) and number (+0.10)
  bonuses, match threshold 0.5 — mirrors RLQ engine. `QuestionText` only in the
  matrix.
- **Per-cell diffing** (novel vs CLQ/RLQ): answer cells keyed by
  `(RowOffset, Column)`; explanation cells keyed by `RowOffset`. A
  `ChangedQuestion` may have `AnswerCellsChanged = true` and/or
  `ExplanationCellsChanged = true` independently of `TextChanged` and
  `NumberChanged`.

## Writer

`HtmlGeneralDataDiffReportWriter` (in `ItrqTool.Infrastructure.Reporting`) renders
`HtmlDiffGeneralDataReportData` to a self-contained interactive HTML report — same
JS-derives pattern and visual language as the CLQ/RLQ `HtmlQuestionDiffReportWriter`,
with its **own copy of the CSS/JS scaffold (duplicate-and-defer; not shared)**. Six
tabs: added / removed / changed / unchanged / current-sheet / previous-sheet.
Multi-row questions render their answer (D/E/F) and explanation (G) cells as a
compact per-cell list; changed questions render a per-cell diff grid (cell coord,
label old→new with word-diff, DV old→new, CF old→new). Registered in
CompositionRoot. The Phase 2a `StubHtmlGeneralDataDiffReportWriter` has been
removed.

The Domain reporting record family consumed by this writer
(`HtmlDiffGeneralDataReportData` and its `HtmlDiffGeneralData*` members) lives in
`src/ItrqTool.Domain/Reporting/HtmlDiffGeneralDataReportData.cs` and the writer
interface `IHtmlGeneralDataDiffReportWriter` in the sibling file — authoritative
for signatures.

## Workflow JSON

```json
{
  "id": "general-data-diff",
  "name": "General Data Diff",
  "tasks": [
    {
      "id": "diff-report",
      "type": "GeneralDataDiff",
      "inputs": {},
      "outputs": { "report": "general-data-diff.html" },
      "parameters": {
        "previousWorkbookFullFilename": "<path to previous year's workbook>",
        "currentWorkbookFullFilename": "<path to current year's workbook>",
        "previousConfigurationFullFilename": "<path to previous year's general-data-structure.json>",
        "currentConfigurationFullFilename": "<path to current year's general-data-structure.json>"
      }
    }
  ]
}
```

## Structure JSON (per-year, runtime input)

```json
{
  "sheetName": "General Data",
  "numberColumn": "B",
  "textColumn": "C",
  "answerColumns": ["D", "E", "F"],
  "explanationColumn": "G",
  "sectionRows": [
    "13:14, 15, 16(1), 17(1), 18(3), 21(2)",
    "26:27(4), 31"
  ]
}
```

Each `sectionRows` entry has the format
`"<sectionRow>:<startRow>(<rowspan>), <startRow>(<rowspan>), ..."` where `rowspan`
is inclusive of the start row (so `18(3)` spans rows 18, 19, 20). The
`(<rowspan>)` is optional and defaults to 1, so `21` is equivalent to `21(1)`.

## Canonical Domain types

```csharp
public sealed record GeneralDataAnswerCell(
    int RowOffset, string Column, string Text,
    string? DvType, string? DvFormula, string? CfOperator);

public sealed record GeneralDataExplanationCell(
    int RowOffset, string Text,
    string? DvType, string? DvFormula, string? CfOperator);

public sealed record GeneralDataQuestion(
    string SectionName,
    string QuestionText,
    string? QuestionNumber,
    int RowNumber,
    IReadOnlyList<string> RowNumberLabels,
    IReadOnlyList<GeneralDataAnswerCell> AnswerCells,
    IReadOnlyList<GeneralDataExplanationCell> ExplanationCells);
```

## Config and parser

`GeneralDataConfig` (in `ItrqTool.Tasks.GeneralDataDiff`) parses its `SectionRows`
strings into `IReadOnlyList<SectionDefinition>` via the `ParsedSections` derived
property. `GeneralDataQuestionParser.Parse(rows, config)` is a public static
method that walks each section's enumerated questions and produces the list of
`GeneralDataQuestion`.

**Cell inclusion rule**: a D/E/F or G cell joins its list iff `TextValue` is
non-empty after trimming (DV/CF on otherwise empty cells does NOT trigger
inclusion). Question text comes from column C of the question's first row only;
continuation rows' column C is ignored.

## Task-internal records (`ItrqTool.Tasks.GeneralDataDiff`)

```csharp
public sealed record AddedQuestion(GeneralDataQuestion Question);
public sealed record RemovedQuestion(GeneralDataQuestion Question);

public sealed record AnswerCellChange(
    int RowOffset, string Column,
    string? OldText, string? NewText,
    string? OldDvType, string? OldDvFormula, string? OldCfOperator,
    string? NewDvType, string? NewDvFormula, string? NewCfOperator,
    bool TextChanged, bool DvChanged, bool CfChanged);

public sealed record ExplanationCellChange(
    int RowOffset,
    string? OldText, string? NewText,
    string? OldDvType, string? OldDvFormula, string? OldCfOperator,
    string? NewDvType, string? NewDvFormula, string? NewCfOperator,
    bool TextChanged, bool DvChanged, bool CfChanged);

public sealed record ChangedQuestion(
    GeneralDataQuestion OldQuestion,
    GeneralDataQuestion NewQuestion,
    double SimilarityScore,
    double? SecondBestSimilarity,
    bool TextChanged, bool NumberChanged,
    bool AnswerCellsChanged, bool ExplanationCellsChanged,
    IReadOnlyList<AnswerCellChange>      AnswerCellChanges,
    IReadOnlyList<ExplanationCellChange> ExplanationCellChanges);

public sealed record UnchangedQuestion(
    GeneralDataQuestion Question,   // new-year question
    double? SecondBestSimilarity,
    int PreviousRowNumber);         // old-year question's row number

public sealed record DiffResult(
    IReadOnlyList<AddedQuestion>     Added,
    IReadOnlyList<RemovedQuestion>   Removed,
    IReadOnlyList<ChangedQuestion>   Changed,
    IReadOnlyList<UnchangedQuestion> Unchanged);
```

## Diff engine

`GeneralDataDiffEngine.Diff(oldQuestions, newQuestions)` in
`ItrqTool.Tasks.GeneralDataDiff`. Mirrors the RLQ engine: base similarity matrix
on `QuestionText`, contextual bonuses (section +0.10, number +0.10), Hungarian
assignment, match threshold 0.5. `SimilarityScore` in results is always the
**base** score (no bonuses — see the base-vs-adjusted invariant in
`diff-task-conventions`).

Per-cell diffing is invoked post-match via `DiffAnswerCells` and
`DiffExplanationCells`:

- Answer cells keyed by `(RowOffset, Column)`.
- Explanation cells keyed by `RowOffset`.
- DV comparison and CF muting (List DV → CF ignored) mirror RLQ/CLQ logic.
- A cell present on one side but absent on the other is reported as an add/remove
  change entry with a null `OldText` or `NewText`.

## Parameters

Five, same shape as CLQ and RLQ:

- `previousWorkbookFullFilename` (required)
- `currentWorkbookFullFilename` (required)
- `previousConfigurationFullFilename` (required)
- `currentConfigurationFullFilename` (required)
- `reportTitle` (optional, defaults to "General Data Diff Report")

## DI registration

`HtmlGeneralDataDiffReportWriter` is registered in
`CompositionRoot.AddItrqToolServices`:

```csharp
services.AddSingleton<IHtmlGeneralDataDiffReportWriter, HtmlGeneralDataDiffReportWriter>();
```

## Architectural note — standalone parser (third-sibling trigger did NOT fire)

General Data deviates from the RLQ pattern by exposing the parser as a standalone
public static class (`GeneralDataQuestionParser`) rather than embedding it as a
private static method on the task class. The "third-sibling abstraction trigger"
did NOT fire: GD's multi-row question structure is sufficiently different from
CLQ/RLQ's one-row-per-question model that sharing parser code would force
accidental coupling. Re-evaluate at the fourth sheet (Risk Level Exposure).

The cross-sheet "Shared DV helpers" / duplicate-and-defer material lives in
`diff-task-conventions`.
