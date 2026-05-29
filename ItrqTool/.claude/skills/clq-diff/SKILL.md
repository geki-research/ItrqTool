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

`AuditQuestion` — see `src/ItrqTool.Tasks/ControlLevelQuestionDiff/AuditQuestion.cs`
(source authoritative for signatures). Carries the parsed question plus the captured
DV/CF fields (`DvType`/`DvFormula`/`CfOperator` and
`DvOperator`/`DvFormula2`/`CfType`/`CfValue`/`CfValue2`); the DV/CF field semantics
(e.g. `DvOperator` null for List/Custom/AnyValue, `DvFormula2` the Between/NotBetween
upper bound) are documented in `diff-task-conventions`.

The question number is parsed out of the cell text prefix by the static helpers on
`AuditQuestion`: `ExtractNumber` returns the leading number token
(`ExtractNumber("1.2) text") → "1.2"`; no prefix → `null`) and `StripPrefix` returns
the text with that prefix removed (`StripPrefix("1.2) text") → "text"`).
`QuestionText` holds the stripped text; `OriginalText` holds the raw cell content.

## DiffResult / ChangedQuestion / UnchangedQuestion

A matched question pair is either Changed or Unchanged — there is **no** separate
ValidationChange category. DV and CF changes are detected on every difference with
no muting (the former List-CF mute has been removed — see `diff-task-conventions`).

`AddedQuestion`, `RemovedQuestion`, `ChangedQuestion`, `UnchangedQuestion`,
`DiffResult` — see `src/ItrqTool.Tasks/ControlLevelQuestionDiff/QuestionDiffResult.cs`
(source authoritative for signatures). `ChangedQuestion` carries the four change
flags: `TextChanged` (true when `SimilarityScore < 1.0`), `NumberChanged`,
`DvChanged`, and `CfChanged` (set on any CF type/operator/value difference — no
muting). The reported `SimilarityScore` / `SecondBestSimilarity` are base
text-similarity scores (see `diff-task-conventions`).

## ControlLevelQuestionsConfig

`SectionDefinition`, `ControlLevelQuestionsConfig` — see
`src/ItrqTool.Tasks/ControlLevelQuestionDiff/ControlLevelQuestionsConfig.cs` (source
authoritative for signatures). Config fields and defaults: `SheetName` (default
`"Control Level Questions"`), `TextColumn` (`"C"`), `InputColumn` (`"D"`),
`ChapterRows`, `SectionRows`, and the computed `ParsedSections` (parses `SectionRows`
into `SectionDefinition`s; throws `FormatException` on the first invalid entry).

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
