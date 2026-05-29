---
name: cell-range-diff
description: Reference for the general-purpose CellRangeDiff task — comparing two Excel workbooks cell-by-cell at the same address over configured A1 ranges (no matching, no sections), reusable across structurally simple worksheets. Load when working on CellRangeDiff, the cell-range diff report, its inline workflow-JSON parameters (file1Path/file2Path, sheet1Name/sheet2Name, ranges, compareScope), address-by-address comparison, the HtmlCellRangeDiffReportWriter, or wiring a simple sheet (e.g. Risk Level Exposure) that does not need a special-purpose diff task.
---

# CellRangeDiff — general-purpose cell-range diff

## What it is
CellRangeDiff is the general-purpose member of the two task families (the special-purpose
per-sheet tasks are ControlLevelQuestionDiff / RiskLevelQuestionDiff / GeneralDataDiff).
It compares two workbooks cell-by-cell at the same address over configured ranges. There is
no fuzzy matching, no sections, no question concept — an address is either changed or
unchanged (a blank counts as a value). Use it for structurally simple sheets where a
special-purpose implementation isn't warranted; one task class serves many such sheets.

TaskType = "CellRangeDiff".

## Configuration — fully inline in the workflow-JSON node
There is no separate config file. All settings are node parameters (ctx.Parameters, string->string):

| Parameter | Meaning |
|---|---|
| file1Path | First workbook (the baseline — left / "old" in the report) |
| file2Path | Second workbook (the comparison — right / "new") |
| sheet1Name | Sheet read from file 1 (may differ from sheet 2's) |
| sheet2Name | Sheet read from file 2 |
| ranges | Semicolon-delimited A1 ranges applied to both sheets, e.g. "B2:F40;H2:H40" |
| compareScope | "Value" or "ValueAndDvCf" — required, no default |
| reportTitle | Optional; blank -> fallback "Cell Range Diff Report" |

Output HTML path comes from the node's outputs.report. A committed placeholder lives at
workflows/cell-range-diff.json; copy it to wire a new simple sheet (e.g. a
risk-level-exposure.json), filling in the parameters.

## Comparison semantics
- Addresses: the union of every cell address in the declared ranges (both files read the
  same ranges, so the address sets match). Cells are reported in row-major order (row
  ascending, then column ascending).
- Value (always): cell text compared after Trim() — leading/trailing whitespace is not
  reported as a change (a documented relaxation of the conservative-input posture; internal
  differences still register).
- DV + CF (only when compareScope == ValueAndDvCf): data-validation and conditional-
  formatting compared via the Shared helpers (DvComparer.IsDvChangedFull,
  CfComparer.IsCfChanged) and displayed via DvDisplayFormatter.FormatFull /
  CfDisplayFormatter.Format (Excel-words display, "—" when none). A cell is changed iff
  text, DV, or CF differs.
- Reads cells through IExcelStructureReader.ReadCells(file, sheet, ranges) — the
  address-driven read added for this task, which (unlike ReadRows) returns every
  address in the range including blank cells and blank cells that carry only DV/CF (e.g. an
  empty dropdown). This is what makes DV/CF comparison trustworthy on data-entry cells.

## Strict validation (conservative-input posture; this task is strict by construction)
Each of these returns TaskResult(Succeeded: false, …) with an Error message — never a silent
default:
- any required parameter missing or blank (all missing ones surfaced together);
- compareScope absent or unrecognised;
- ranges empty after parsing, or any token failing the A1 shape;
- file1Path / file2Path not found;
- sheet missing (the reader throws -> surfaced as Error).

OperationCanceledException always propagates.

## Report shape
HTML written by HtmlCellRangeDiffReportWriter (Infrastructure). Header: title, both file
paths, both sheet names, generated timestamp, changed/unchanged counts. A Changed table and
an Unchanged table; columns Address | File 1 | File 2, plus
File 1 DV | File 2 DV | File 1 CF | File 2 CF only when compareScope == ValueAndDvCf
(IncludeValidationFormatting on the report data drives this).

## Source pointers
- Task: src/ItrqTool.Tasks/Tasks/CellRangeDiffTask.cs
- Engine + types: src/ItrqTool.Tasks/CellRangeDiff/ (CellRangeDiffEngine, CompareScope,
  CellRangeDiffResult + ChangedCell / UnchangedCell, all public for test reference)
- Reporting records + writer interface: src/ItrqTool.Domain/Reporting/HtmlDiffCellRangeReportData.cs,
  …/IHtmlCellRangeDiffReportWriter.cs
- Writer: src/ItrqTool.Infrastructure/Reporting/HtmlCellRangeDiffReportWriter.cs
- Reader primitive: IExcelStructureReader.ReadCells (impl in ClosedXmlExcelStructureReader)

## Parked enhancement — "*" wildcard range
A "*" token meaning "the whole worksheet" is a parked idea (feasible). When built: expand it
in the reader (ReadCells) to worksheet.RangeUsed(...) with options that include
DV/CF-only cells (else it reintroduces the blank-cell blind spot); relies on the engine's
union/absent-as-empty path since the two sheets' used ranges may differ; and should suppress
addresses blank in both files to avoid flooding "unchanged" on sparse sheets. Watch
per-cell-scan performance on large used ranges.
