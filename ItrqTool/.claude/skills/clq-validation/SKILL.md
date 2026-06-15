---
name: clq-validation
description: CLQ_v01 validation task (ControlLevelQuestionValidation_v01Task, TaskType "ControlLevelQuestionValidation_v01"). Within-year checks (structure, frozen values/constraints, input validity) and cross-year identity checks against the previous-year response. The 18 ClqFinding catalogue (ids, default severities, ValidationCheck mapping, gating rules), config shape (columns D/E/F/H/I/J/M/N, sectionRows format, AllowedAnswers, DeviationThreshold, SeverityOverrides), PatchAnswerDv mechanics, the file-source convention (StaticFileSource now / DynamicFileSource later), the five-node trial workflow topology, and the integration trial (193 real-question trio, 23 seeded findings). Load when working on the CLQ_v01 validation task, its config, the alignment engine, the check catalogue, or the trial scenario.
---

# CLQ_v01 — Control-Level-Question validation

`ControlLevelQuestionValidationV01Task` (TaskType `"ControlLevelQuestionValidation_v01"`)
validates a current-year Control-Level-Question response against the empty auditor template
(within-year: structural integrity, frozen values, and frozen constraints) and the previous-year
response (cross-year: question identity). It emits a `ValidationReport` JSON consumed by the
downstream `FeedbackChecklistAssembler`.

## Task contract

| | |
|---|---|
| **TaskType** | `ControlLevelQuestionValidation_v01` |
| **Parameter** | `configurationFullFilename` — path to the CLQ_v01 config JSON (relative paths resolved against CWD, NOT `AppContext.BaseDirectory` — unlike `StaticFileSource`/assembler) |
| **Input** `currentResponse` | Current-year response workbook path (wired from the file-source task) |
| **Input** `emptyTemplate` | Empty auditor template workbook path (wired from file-source) |
| **Input** `previousResponse` | Previous-year response workbook path (wired from file-source) |
| **Output** `report` | `ValidationReport` JSON (serialized via `ValidationReportSerializer`) |

**`Succeeded:false`** is emitted ONLY for an unreadable/missing file or an invalid/under-specified
config. A `Fatal` finding is data in the report — it does NOT fail the task.

## Config (`ControlLevelQuestionValidationV01Config`)

One config governs all three workbooks (they share the same internal format and sheet name).

| Field | Type | Meaning |
|---|---|---|
| `SheetName` | string | Sheet to read in all three workbooks |
| `TextColumn` | string | Column letter for question text / D by default in production |
| `GuidanceColumn` | string | Guidance column / E |
| `PreviousAnswerColumn` | string | Injected prior-year answer (frozen) / F |
| `AnswerColumn` | string | Current-year answer (input) / H |
| `StrengthsColumn` | string | Strengths narrative / I |
| `WeaknessesColumn` | string | Weaknesses narrative / J |
| `ProvidedByColumn` | string | Responding org unit / M |
| `XrefIdColumn` | string | Cross-reference identity key / N |
| `ChapterRows` | `int[]` | Row numbers of chapter-header rows |
| `SectionRows` | `string[]` | Section definitions — see format below |
| `AllowedAnswers` | `string[]` | Valid answer values (ordinal comparison) |
| `DeviationThreshold` | int | Minimum `|current − previous|` to flag deviation (must be > 0) |
| `SeverityOverrides` | `dict<string, FindingEvaluation>` | Per finding-id severity override; all keys must be valid finding-ids |

**`SectionRows` format:** `"<sectionRow>:<firstQuestionRow>-<lastQuestionRow>"` — same format as
the CLQ-diff config. `sectionRow` must be a positive integer; `firstQuestionRow > sectionRow`;
`lastQuestionRow ≥ firstQuestionRow`. A malformed entry throws `FormatException`, caught by the
loader → `Succeeded:false`.

**Loader** (`ControlLevelQuestionValidationV01ConfigLoader.Load`): strict, fail-loud.
`UnmappedMemberHandling.Disallow` (unknown JSON keys rejected), bad enum values fail, blank column
letters fail, empty `AllowedAnswers` fail, non-positive `DeviationThreshold` fails, unknown
`SeverityOverrides` keys fail. All errors collected; throws `ClqConfigException` on any problem.

### Production config (`configs/clq-v01-validation-config.json`)

Sheet `"IT Risk Control Self-Assessment"`, columns D/E/F/H/I/J/M/N as above,
7 chapter rows `[4, 51, 86, 145, 177, 191, 205]`, 30 section ranges,
`AllowedAnswers ["1","2","3","4"]`, `DeviationThreshold 2`, `SeverityOverrides {}`.

## The 18 findings

Every finding is identified by a `ClqFinding` enum member (source of truth: `ClqFindings`
descriptor table in `ClqFinding.cs`). Default severities may be overridden per finding-id in
`SeverityOverrides`.

### Malformed keys (across all three workbooks)

| Finding enum | Id | Default | Check | Meaning |
|---|---|---|---|---|
| `XrefIdEmptyOrDuplicated` | `structure.xrefid-empty-or-duplicated` | **Fatal** | Structure | Identity key in a question row is blank or duplicated within its workbook; the question cannot be reliably matched |

Emitted once per malformed key. All dependent checks for that current row are skipped (`NotEvaluatedMalformedKey`).

### Within-year: structure (template vs response)

| Finding enum | Id | Default | Check | Meaning |
|---|---|---|---|---|
| `QuestionRemoved` | `structure.question-removed` | Error | Structure | Template question (by identity key) absent from response |
| `QuestionAdded` | `structure.question-added` | Error | Structure | Response question absent from template; row not declared by template |
| `QuestionRowShifted` | `structure.question-row-shifted` | Error | Structure | Matched by identity key but on a different row than the template |
| `NumberFormatUnrecognized` | `structure.number-format-unrecognized` | Warning | Structure | Question-number prefix is not a recognised two-level form (e.g. `1.2)`) |

### Within-year: frozen values

| Finding enum | Id | Default | Check | Meaning |
|---|---|---|---|---|
| `ReferenceTextAltered` | `static-cell.reference-text-altered` | Warning | FrozenValue | Frozen text (question text, guidance, chapter, section, identity-key text) differs from template; one finding lists all differing fields |
| `PreviousAnswerAltered` | `static-cell.previous-answer-altered` | Warning | FrozenValue | Col-F injected prior answer differs from prior year's actual answer (col H of previous workbook) |

**Gating — `PreviousAnswerAltered`:** runs on **every Agree row**, independently of whether the
current-year answer is filled in or valid. It is a frozen-value check on col F, not an input-validity
check.

### Within-year: frozen constraint

| Finding enum | Id | Default | Check | Meaning |
|---|---|---|---|---|
| `AnswerValidationRuleChanged` | `constraint.answer-validation-rule-changed` | Error | FrozenConstraint | Answer-cell DV rule differs from template (compared via `DvComparer.IsDvChangedFull`) |

### Input validity (current answer)

All three of these are **gated on the question being structurally joined** (`JoinedByXrefId` or
`AddedInResponse`) — not on a malformed key.

| Finding enum | Id | Default | Check | Meaning |
|---|---|---|---|---|
| `AnswerMissing` | `input-cell.answer-missing` | Error | MissingResponse | Answer cell empty |
| `AnswerNotInAllowedSet` | `input-cell.answer-not-in-allowed-set` | **Fatal** | MissingResponse | Answer present but not in `AllowedAnswers`; blocks strengths/weaknesses/deviation checks for this row |
| `StrengthsMissing` | `input-cell.strengths-missing` | Error | MissingResponse | Answer requires strengths but col-I cell empty |
| `WeaknessesMissing` | `input-cell.weaknesses-missing` | Error | MissingResponse | Answer requires weaknesses but col-J cell empty |

**Gating — strengths/weaknesses:** gated on `answerUsable` (answer ∈ `AllowedAnswers` and
non-empty). The I/J matrix (production config `["1","2","3","4"]`):
- Answer `"1"`: strengths required, weaknesses not required
- Answer `"2"` or `"3"`: both required
- Answer `"4"`: weaknesses required, strengths not required

### Cross-year identity

Determined by `ClqAlignmentEngine`: key + text are compared to find a confident previous
counterpart. `PreviousMatch` (used for F-integrity and deviation) is set **only on `Agree`** —
the two attention-raising outcomes below never auto-use a previous question as a baseline.

| Finding enum | Id | Default | Check | Case | Meaning |
|---|---|---|---|---|---|
| `XrefIdConflict` | `cross-year.xrefid-conflict` | Error | Structure | Case 2 | Key points to previous row A but text confidently matches previous row B (A≠B); human must verify |
| `NewXrefIdResemblesPrevious` | `cross-year.new-xrefid-resembles-previous` | Warning | Structure | Case 3 | New key absent from previous year, but a textual twin exists; may be a rescope or a mistyped key |
| `SameXrefIdTextDiverged` | `cross-year.same-xrefid-text-diverged` | Warning | Structure | Case 4 | Same key as a previous question, but text diverged below match threshold; may be a heavy rewrite or a reused key |
| `NoPreviousBaseline` | `cross-year.no-previous-baseline` | Information | Structure | Case 5 | No previous counterpart by key or text; ordinary new/orphan question |
| `AnswerDeviation` | `cross-year.answer-deviation` | Warning | Deviation | Case 1 (Agree) | `|current − previous| ≥ DeviationThreshold`; displayed as `prevValue → curValue` |
| `PreviousAnswerUnusable` | `cross-year.previous-answer-unusable` | Information | Deviation | Case 1 (Agree) | Confident match exists and current answer is usable, but previous answer is empty or not in allowed set → deviation cannot be evaluated |

**Gating — deviation:** gated on `answerUsable` (current) AND previous answer being
int-parseable AND ∈ `AllowedAnswers`. If usable, deviation fires only when
`Math.Abs(cur − prev) >= DeviationThreshold`.

**Gating — `PreviousAnswerUnusable`:** only reached when `answerUsable` is true AND the matched
previous answer is unusable.

## `PatchAnswerDv` — answer-DV sourcing

After parsing each workbook via `InternalClqQuestionParser.Parse`, `PatchAnswerDv` re-reads the
answer-column DV from `IExcelStructureReader.ReadCells` (address-driven, range
`{AnswerColumn}{firstRow}:{AnswerColumn}{lastRow}`). This is required because `ReadRows`/`CellsUsed`
skips blank cells, so blank template H cells that carry a DV-list rule would otherwise have null DV.
Applied to all three workbooks before alignment.

## File-source convention

Internal-format workflows begin with a file-source task that emits a resolved file path on its
`output` key. The current implementation uses `StaticFileSource`; a future `DynamicFileSource`
(file-picker dialog, path resolved at runtime) will carry the **same output contract** — it emits
a resolved path, not file content. The swap is a pure rewiring of the three file-source nodes with
zero changes to `ControlLevelQuestionValidationV01Task`. The validation task always receives its
three workbooks via wired inputs, never via a hardcoded path.

## Workflow topology

```
load-current-response  (StaticFileSource → clq_current_response.xlsx)   ──┐
load-empty-template    (StaticFileSource → clq_empty_template.xlsx)      ──┤→ validate (ControlLevelQuestionValidation_v01) → assemble (FeedbackChecklistAssembler)
load-previous-response (StaticFileSource → clq_previous_response.xlsx)  ──┘
```

The assembler receives `validate.report` on input key `findings01` and produces the feedback
checklist as an **`.xlsx`** workbook via `ClosedXmlFeedbackChecklistWriter` (not HTML). Its config
(`configs/feedback-checklist-assembler-config.json`) points to
`templates/feedback-checklist-template.xlsx` and maps the nine `ChecklistColumn` fields to columns
A–I of the "Checklist" sheet.

Committed workflow file: `workflows/clq-v01-validation-trial.json`.

## Trial / worked example

Integration tests in `tests/ItrqTool.Integration.Tests/ClqV01/`:

- **`ClqV01BaselineFactory`** builds a zero-findings trio from the config structure, using 193
  **real audit question texts** loaded from the embedded resource
  `ClqV01/clq-v01-trial-questions.txt`. Texts are globally distinct so the cross-year Hungarian
  assignment is unambiguous (no ties for the matcher to break).
- **`ClqV01TrialScenario`** applies perturbations to the baseline: 10 local rows (11 findings
  covering all input-validity and within-year checks) plus 6 structural rows (12 findings covering
  all cross-year identity outcomes) → **23 findings total**.
- **`ClqV01TrialTests.FullScenario_TenLocalPlusSixStructuralRows_ExactFindings_NoExtras`** runs the
  task directly and asserts the exact finding set (no extras, no missing).
- **`ClqV01EndToEndWorkflowTests.TrialWorkflow_FullScenario_WorkflowSucceedsAndChecklistHasFindings`**
  runs the committed trial workflow through the full engine (all five tasks) and asserts the
  workflow succeeds, the checklist XLSX is produced, and findings (including H23 `MissingResponse`
  and at least one `Fatal`) flow through to the checklist. Trial artifacts emitted to
  `trial-output/clq-v01/` (git-ignored) for human inspection.
