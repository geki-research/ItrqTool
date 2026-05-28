---
name: rlq-diff
description: Risk Level Questions sheet diff task (RiskLevelQuestionDiffTask, TaskType "RiskLevelQuestionDiff"). The RLQ config format (RiskLevelQuestionsConfig, numberColumn/textColumn/answerColumn/explanationColumn), the RiskLevelQuestion record (no ChapterName/OriginalText; number in its own column B), parser specifics, the ExplanationChanged flag, the locked design decision that ExplanationPrompt does NOT participate in the matching matrix, the five parameters, and the report shape (shares HtmlQuestionDiffReportWriter with CLQ plus an explanation diff block). Load when working on the Risk Level Questions diff, its config, parser, engine, or records. See also diff-task-conventions for the shared matching/DV/CF machinery.
---

# RLQ — Risk Level Questions diff

The second diff task. Compares the Risk Level Questions sheet between two
reference years. Structurally similar to CLQ but with deliberate divergences.
Shared matching/DV/CF machinery is in the `diff-task-conventions` skill.

- **Task type**: `RiskLevelQuestionDiff`
- **Namespace**: `ItrqTool.Tasks.RiskLevelQuestionDiff`
- **Default sheet name**: `"Risk Level Questions"`
- **Default output filename**: `risk-level-question-diff.html`
- **Default report title**: `"Risk Level Questions Diff Report"`

## Records

- `RiskLevelQuestion` — `(SectionName, QuestionText, ExplanationPrompt,
  QuestionNumber, RowNumber, DvType, DvFormula, CfOperator, DvOperator = null,
  DvFormula2 = null, CfType = null, CfValue = null, CfValue2 = null)`. No
  `ChapterName` (RLQ has sections only). No `OriginalText` (number is in its own
  column, no prefix to strip). The five new fields mirror the same capture fields
  on `ExcelCellStructure`; see `diff-task-conventions` for semantics.
- `RiskLevelQuestionsConfig` — `SheetName`, `NumberColumn` (default `"B"`),
  `TextColumn` (default `"C"`), `AnswerColumn` (default `"D"`),
  `ExplanationColumn` (default `"E"`), `SectionRows`, computed `ParsedSections`.
  No `ChapterRows`.
- Result records `AddedQuestion`, `RemovedQuestion`, `ChangedQuestion`,
  `UnchangedQuestion`, `DiffResult` live in the same namespace as siblings to
  CLQ's. `ChangedQuestion` has an extra `ExplanationChanged` flag compared to
  CLQ's.

## Configuration file format

```json
{
  "sheetName": "Risk Level Questions",
  "numberColumn": "B",
  "textColumn": "C",
  "answerColumn": "D",
  "explanationColumn": "E",
  "sectionRows": ["3:4-15", "16:17-42"]
}
```

`sectionRows` uses the same `"<sectionRow>:<firstQuestionRow>-<lastQuestionRow>"`
format as CLQ. Throws `FormatException` on invalid entries (propagates through
`ExecuteAsync`'s outer catch as a `TaskResult.Succeeded: false`).

## Parser specifics

- Question number read directly from column B. No regex. Trimmed string stored
  as-is; `null` if cell missing or whitespace.
- Question text from column C (no prefix strip).
- Explanation prompt from column E.
- DV/CF metadata from column D, same `IExcelStructureReader` contract as CLQ.
- Section name read from column C on the row indicated by
  `SectionDefinition.SectionRow`.

## Diff engine

`ItrqTool.Tasks.RiskLevelQuestionDiff.QuestionDiffEngine.Diff(prev, cur)`.
Mirrors CLQ's engine with one structural difference: an `explanationChanged`
flag is computed alongside `textChanged`, `numberChanged`, `dvChanged`,
`cfChanged`, and flows into the result's `ChangedQuestion`.

The matching matrix uses `QuestionText` only with the existing +0.10
section-match and +0.10 number-match contextual bonuses.
**`ExplanationPrompt` does NOT participate in the matching matrix.** This is a
deliberate decision: keeping the matching surface narrow preserves the "reported
similarity is base text similarity" invariant. If a future failure mode shows
that explanation similarity would have disambiguated text-similar questions,
adding an `ExplanationBonus` is a one-line matrix-construction change.

### Matching surface narrowness — `ExplanationPrompt` in RLQ (locked design note)

The RLQ diff engine's matching matrix uses `QuestionText` only, with the same
section/number contextual bonuses as CLQ. `ExplanationPrompt` (the second text
field per row in the RLQ schema) does NOT participate in matching; the
`ExplanationChanged` flag is computed post-match from a separate
`TextSimilarity.Score` call on the matched pair's explanation strings.
Rationale: keeps the "reported similarity is base question-text similarity"
invariant clean, and avoids the question of whether explanation similarity should
affect the reported `SimilarityScore` (it should not). Symmetric extensions are
reserved for the matching matrix only; secondary text fields stay outside it.

## Parameters

Five, same shape as CLQ:

- `previousWorkbookFullFilename` (required)
- `currentWorkbookFullFilename` (required)
- `previousConfigurationFullFilename` (required)
- `currentConfigurationFullFilename` (required)
- `reportTitle` (optional, defaults to "Risk Level Questions Diff Report")

## Report shape

RLQ produces the same interactive HTML report shape as CLQ (shares
`HtmlQuestionDiffReportWriter`), with the addition of an **explanation diff block
per changed question** (rendered inline in two columns on the Changed tab, driven
by the `ExplanationChanged` flag and the `OldExplanation` / `NewExplanation`
fields on the report's `HtmlDiffChangedQuestion`). The sheet-order tabs behave as
described in `diff-task-conventions`.

## Workflow JSON

Placeholder at `workflows/risk-level-question-diff.json`. Same shape as CLQ's
workflow JSON with the task type, output filename, and placeholder parameter
paths adapted for RLQ.
