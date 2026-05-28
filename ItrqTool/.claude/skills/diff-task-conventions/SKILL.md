---
name: diff-task-conventions
description: Cross-sheet diff machinery shared by all questionnaire diff tasks (CLQ, RLQ, General Data). The matching-matrix design (QuestionText-only base similarity, +0.10 section and +0.10 number contextual bonuses, Hungarian assignment, base-vs-adjusted similarity invariant, match threshold 0.5); DV (data validation) and CF (conditional formatting) capture, display, and comparison rules; the src/ItrqTool.Tasks/Shared helper inventory (DvDisplayFormatter, DvComparer); the duplicate-and-defer implementation philosophy and the 4th-sheet (Risk Level Exposure) abstraction-extraction trigger. Load when building or modifying any diff task, its diff engine, similarity matrix, or DV/CF handling.
---

# Diff task conventions (cross-sheet)

This skill documents the machinery shared by every questionnaire diff task —
`ControlLevelQuestionDiffTask` (CLQ), `RiskLevelQuestionDiffTask` (RLQ), and
`GeneralDataDiffTask` (GD). Per-sheet specifics live in `clq-diff`, `rlq-diff`,
and `gd-diff`. This is the home that Phase B.2 will extend with the full DV+CF
capture rules.

The Domain reporting record families consumed by the HTML writers live under
`src/ItrqTool.Domain/Reporting/` (authoritative for signatures):
`HtmlDiffReportData.cs` (CLQ/RLQ report shape), `IHtmlReportWriter.cs`,
`HtmlDiffGeneralDataReportData.cs` (GD report shape),
`IHtmlGeneralDataDiffReportWriter.cs`.

---

## The matching matrix (shared by all three diff engines)

Each diff engine builds a similarity matrix between previous-year and
current-year questions, then runs Hungarian assignment to pick the optimal
one-to-one matching.

- **Base similarity is computed on `QuestionText` only.** No other text field
  participates in the matrix.
- **Contextual bonuses** are added on top of the base score during matrix
  construction:
  - **+0.10** when the two questions share the same section.
  - **+0.10** when the two questions share the same question number.
- **Hungarian assignment** selects the optimal matching from the bonused matrix.
- **Match threshold 0.5**: a candidate pair below this (bonused) score is not
  matched — the questions fall into Added / Removed instead.

### The base-vs-adjusted similarity invariant (lesson 11)

**The `SimilarityScore` reported in every result record is always the BASE
score — never the bonused/adjusted score used for assignment.** The bonuses
exist only to disambiguate the matching; they must not leak into the number the
user sees. `SecondBestSimilarity` likewise reports a base score. Keeping the
reported similarity equal to base text similarity is a locked invariant: it
keeps "reported similarity is base question-text similarity" true across all
sheets. Symmetric extensions (e.g. an `ExplanationBonus`) are reserved for the
matrix-construction step only; they never change the reported score.

Per-sibling the engines are duplicated: CLQ's `QuestionDiffEngine`, RLQ's
`QuestionDiffEngine`, and `GeneralDataDiffEngine`. The Hungarian-algorithm
helper and `TextSimilarity` are likewise duplicated per-sibling (see
duplicate-and-defer below).

---

## DV / CF capture, display, and comparison

Diff tasks read raw cell content and Excel structural metadata via
`IExcelStructureReader` (Domain), which exposes per-cell:

- `DataValidationType` — name of the ClosedXML `XLAllowedValues` enum value, or
  null if no data validation applies to the cell.
- `DataValidationFormula` — raw DV formula string (`dv.Value` from ClosedXML).
  Inline lists look like `"\"Yes,No,N/A\""`; range refs look like
  `"Sheet!$A$1:$A$5"`. Null if no DV rule applies.
- `ConditionalFormattingOperator` — name of the ClosedXML `XLCFOperator` enum
  value for the first matching conditional format, or null if none applies.

(`ExcelCellStructure` / `ExcelRowStructure` signatures:
`src/ItrqTool.Domain/IExcelStructureReader.cs`.)

### Display

DV is rendered for display as one of: `"—"` (no DV), the bare type name, or
`"List: A | B | C"` for inline-list DVs (the list values parsed out of the
formula). This is the format stored in the `DvDisplay` fields of the reporting
records.

### Comparison and CF muting

- **CF changes are ignored (muted) when the old DvType is `"List"`.** A
  list/dropdown cell routinely carries presentational conditional formatting
  that is noise, not signal — so `CfChanged` is forced **false** when the old
  DvType == `"List"`. This rule is shared by CLQ, RLQ, and GD.
- DV comparison treats inline lists by value equality of their parsed members,
  not raw-formula string equality.

---

## Shared helpers — `src/ItrqTool.Tasks/Shared/`

These were extracted from the three per-sheet copies into `ItrqTool.Tasks.Shared`
**only after proving the three copies were byte-identical**:

- `DvDisplayFormatter` — DV display formatting (the `"—"` / type-name /
  `"List: …"` rendering above).
- `DvComparer` — comparison logic: `IsDvChanged`, list equality, inline-list
  detection.

**Phase B will add** `CfDisplayFormatter` and `CfComparer` here once the CF
capture rules are fully specified; leave room for them in this inventory.

The remaining per-sibling helpers — the Hungarian algorithm, `TextSimilarity`,
the question parser, and the diff engine — **remain duplicated per-sibling**.
Their structures diverge too much for safe shared extraction. The CSS/JS
scaffold in the HTML writers also remains duplicate-and-defer (not shared).

---

## Sheet-order tabs (shared HTML writer behaviour)

The CLQ/RLQ writer (`HtmlQuestionDiffReportWriter`) and the GD writer each render
two additional tabs, **"Current sheet"** and **"Previous sheet"**, that show
every question of the respective workbook in sheet-row order. Per entry: a status
badge (added / changed / unchanged on the current side; removed / changed /
unchanged on the previous side), inline section/chapter separator rows, and
click-to-expand detail cards. Entry data is **derived in JS at init time** from
the existing `added` / `removed` / `changed` / `unchanged` arrays — there are no
new JSON fields. Filter behaviour: separators are always visible; detail rows
track their parent entry's filter state.

---

## Duplicate-and-defer philosophy & the abstraction-extraction trigger

The codebase deliberately tolerates duplication across the sibling diff tasks
rather than abstracting prematurely. Shared code is extracted **only** after a
duplication is proven identical and stable (as with `DvDisplayFormatter` /
`DvComparer`). Divergent structures (engines, parsers, Hungarian, TextSimilarity,
HTML scaffold) stay duplicated.

**Abstraction-extraction trigger: the 4th sheet (Risk Level Exposure).** When the
fourth diff sheet arrives, re-evaluate which per-sibling helpers have converged
enough to share. The "third-sibling trigger" did NOT fire for General Data's
parser (its multi-row model differs too much from the one-row-per-question model
of CLQ/RLQ — see `gd-diff`); the fourth sheet is the next decision point.
