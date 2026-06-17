---
name: clq-validation-v02
description: CLQ_v02 validation task (ControlLevelQuestionValidationV02Task, TaskType "ControlLevelQuestionValidation_v02"), the first production consumer of the version-neutral QuestionnaireValidation core. Within-year checks (structure, frozen values/constraints, input validity) plus cross-year identity against the previous-year response, reproducing v01's 18-finding CLQ baseline catalogue PLUS three new answer-stability findings on the inserted column K. Config shape (columns D/E/F/H/I/J/K/N/O — K inserted, provided-by shifted to N, xref-id to O vs v01's M/N; AllowedAnswers, AllowedStabilityAnswers, sectionRows, DeviationThreshold, SeverityOverrides), the generic core surfaces it composes (ValidationPipeline.Run<T>, ClqV02Profile, DvPatcher, AlignmentEngine<T>, ClqBaselineChecks, RequiredInputCell/FrozenConstraintCell extension primitives, the FindingCatalogue, LayoutParser), the five-node trial workflow, and the synthetic trial (193-question corpus, baseline-zero + 3-finding stability exact-set + end-to-end). Load when working on the CLQ_v02 validation task, the generic questionnaire-validation core, or extending a validator with a new within-year input column.
---

# CLQ_v02 — Control-Level-Question validation (first citizen of the generic core)

`ControlLevelQuestionValidationV02Task` (TaskType `"ControlLevelQuestionValidation_v02"`, namespace
`ItrqTool.Tasks`) validates a current-year Control-Level-Question response against the empty auditor
template (within-year) and the previous-year response (cross-year), emitting a `ValidationReport` JSON
consumed by the downstream `FeedbackChecklistAssembler`. It is the **first production consumer of the
version-neutral validation core** (`ItrqTool.Tasks.QuestionnaireValidation`). v01 (the `clq-validation`
skill) is a separate **frozen-legacy bespoke** stack; v02 is the core citizen.

## The generic core (`ItrqTool.Tasks.QuestionnaireValidation`)

`ValidationPipeline.Run<T>` wires: read (`IExcelStructureReader`) → parse (`QuestionParser` via the
profile's `RecordFactory`; `QuestionNumberParser` for prefix extract/strip) → DV-patch (`DvPatcher`,
one pass per DV-role) → align (`AlignmentEngine.Align<T>` where `T : IAlignmentIdentity`; copied-verbatim
`TextSimilarity` + `HungarianAlgorithm`, SectionBonus/NumberBonus 0.10, MatchThreshold 0.50) → baseline
checks (`ClqBaselineChecks.Run` — the 18 CLQ findings) + the profile's extension primitives →
`IReadOnlyList<ValidationFinding>`, wrapped in a `ValidationReport`. Sub-namespaces: `…/Alignment`,
`…/Checks`, `…/Parsing`, `…/Config`, `…/Findings`.

Layout is parsed by `LayoutParser.Parse(chapterRows, sectionRows, chapterNameColumn, sectionNameColumn,
questionTextColumn)` (ns `…QuestionnaireValidation.Config`) → `QuestionnaireLayout(QuestionTextColumn,
Chapters, Sections)` (ns `…Parsing`) with `LayoutSection(SectionRow, FirstQuestionRow, LastQuestionRow,
NameColumn)`. **Two-phase config validation:** structural errors at `ConfigLoader.Load<TConfig>(json,
validate)` (the validate delegate → `ConfigException`); severity-override-key errors later inside
`ValidationPipeline.Run` (validated against the assembled `FindingCatalogue`).

`ValidationReport` / `ValidationReportSerializer` live in `ItrqTool.Tasks.Validation`. `ValidationFinding`
(ns `ItrqTool.Domain.Validation`) has 8 fields: `Check` (`ValidationCheck`), `Evaluation`
(`FindingEvaluation`), `CellAddresses`, `QuestionNumber`, `QuestionText`, `RequestedData`, `ProvidedBy`,
`CheckResult`.

## Task contract

| | |
|---|---|
| **TaskType** | `ControlLevelQuestionValidation_v02` |
| **Parameter** | `configurationFullFilename` — path to the v02 config JSON (used verbatim; relative paths resolved against CWD, NOT `AppContext.BaseDirectory`) |
| **Input** `currentResponse` | Current-year response workbook |
| **Input** `emptyTemplate` | Empty auditor template workbook |
| **Input** `previousResponse` | Previous-year response workbook |
| **Output** `report` | `ValidationReport` JSON (via `ValidationReportSerializer`) |

`Succeeded:false` ONLY for an unreadable/missing file or invalid/under-specified config. A `Fatal`
finding is data in the report — it does NOT fail the task. Ctor injects `IExcelStructureReader` +
`ILogger<…>`; config loads via `ConfigLoader.Load<ControlLevelQuestionValidationV02Config>(json, c => c.Validate())`
(the generic loader, NOT a v02-specific loader class); then `ClqV02Profile.Build(config)` →
`ValidationPipeline.Run` → serialize.

## Config (`ControlLevelQuestionValidationV02Config : IClqBaselineConfig`)

One config governs all three workbooks. Fields: `SheetName`; 9 column letters; `ChapterRows`
(`IReadOnlyList<string>` — string, unlike v01's int); `SectionRows` (`"<sectionRow>:<first>-<last>"`,
parsed at RUN by `LayoutParser` → `FormatException` on bad format, caught → `Succeeded:false`; there is
**no `ParsedSections`** computed property, unlike v01); `AllowedAnswers`; `AllowedStabilityAnswers`;
`DeviationThreshold`; `SeverityOverrides`. `Validate()` runs 13 structural rules; strict/fail-loud via
`ConfigLoader.Load` (`UnmappedMemberHandling.Disallow`, all errors collected). Override-key validation
happens later, in `ValidationPipeline.Run`.

**Column map (the K-insert vs v01):**

| role | column |
|---|---|
| text / guidance / previous-answer | D / E / F |
| answer / strengths / weaknesses | H / I / J |
| **answer-stability** (new in v02) | **K** |
| provided-by | **N** (was M in v01) |
| xref-id | **O** (was N in v01) |

### Production config (`configs/clq-v02-validation-config.json`)

Sheet `"IT Risk Control Self-Assessment"`; D/E/F/H/I/J + K/N/O; `ChapterRows
["4","51","86","145","177","191","205"]` (quoted strings); 30 `SectionRows`; `AllowedAnswers
["1","2","3","4"]`; `AllowedStabilityAnswers ["Yes","No"]`; `DeviationThreshold 2`; `SeverityOverrides {}`.
Pinned by `ClqV02ProductionConfigAssetTests` (all values + `LayoutParser.Parse(...).Sections.Count == 30`).

## Profile (`ClqV02Profile.Build(config) → ValidationPipelineProfile<ClqV02Question>`)

- **Record** `ClqV02Question : IAlignmentIdentity` — 23 fields = v01's 18 + `AnswerStability` +
  `AnswerStabilityDvType/Formula/Operator/Formula2`.
- **Layout** via `LayoutParser.Parse(ChapterRows, SectionRows, TextColumn, TextColumn, TextColumn)`.
- **RecordFactory** reads the payload columns (incl. K/N/O); leaves all 8 DV fields null (DvPatcher fills them).
- **DvRoles** (2): answer → **H**, answer-stability → **K**.
- **RunBaseline** closes over a `ClqBaselineRoleMap` whose `AnswerDv*` selectors read the **answer** DV;
  reproduces v01's 18 findings exactly (same ids/severities/gating).
- **Extensions** (2): `RequiredInputCell` + `FrozenConstraintCell`, both `role:"answer-stability"`,
  `allowed` from `config.AllowedStabilityAnswers`, DV from the answer-stability DV fields.

## Findings

**Baseline (18):** the CLQ catalogue (`ClqBaselineFindings.All`) — a one-time copy of v01's 18 findings
(ids, default severities, `ValidationCheck` mapping, gating), shared across CLQ versions, NOT referencing
v01's `ClqFinding` enum. Gating preserved verbatim (F-integrity on every Agree row; strengths/weaknesses +
deviation gated on a usable answer; deviation also on previous int-parseable ∈ AllowedAnswers).

**Stability (3, new in v02):**

| Finding id | `ValidationCheck` | default `Evaluation` | fires when | `CellAddresses` |
|---|---|---|---|---|
| `input-cell.answer-stability.missing` | `MissingResponse` | `Error` | K blank — **unconditional per current row** | `K{row}` |
| `input-cell.answer-stability.not-in-allowed-set` | `MissingResponse` | `Fatal` | K present but ∉ `AllowedStabilityAnswers` | `K{row}` |
| `constraint.answer-stability.validation-rule-changed` | `FrozenConstraint` | `Error` | current K-DV differs from template K-DV | `K{row}` |

The string ids are internal routing keys (from `role:"answer-stability"`); they are NOT fields on the
emitted finding — assert on `Check` + `Evaluation` + `CellAddresses` + a `CheckResult` substring. All three
are config-overridable via `SeverityOverrides`.

## Workflow topology

`workflows/clq-v02-validation-trial.json`: `StaticFileSource ×3` (sources
`trial-workbooks/clq-v02/{clq_current_response,clq_empty_template,clq_previous_response}.xlsx`) →
`ControlLevelQuestionValidation_v02` (→ `clq-v02-validation-report.json`) → `FeedbackChecklistAssembler`
(config `configs/feedback-checklist-assembler-config.json`, shared/version-agnostic →
`clq-v02-feedback-checklist.xlsx`).

## Trial / worked example (`tests/ItrqTool.Integration.Tests/ClqV02/`)

- `ClqV02BaselineFactory.Build(config)` — a fully-consistent trio over the **reused 193-question corpus**
  (the `ClqV01` embedded resource); current/previous `AnswerStability "Yes"`, template null → **zero findings**.
  Derives sections from `LayoutParser` (no `ParsedSections`); parses `ChapterRows` string→int before ordering.
- `ClqV02WorkbookWriter.Write(path, sheet, descriptor, answerDvOverrides?, stabilityDvOverrides?)` — writes
  D/E/F/H/I/J/**K**/**N**/**O**; applies a `"1,2,3,4"` DV on H and a `"Yes,No"` DV on K to every row of every
  workbook (each overridable per-row, current-only, for perturbations).
- `ClqV02BaselineTests` — zero findings through the task. `ClqV02StabilityPerturbationTests` — exact-set:
  current rows 7/8/9 → `{missing@K7 (Error), not-in-allowed-set@K8 (Fatal), rule-changed@K9 (Error)}`, no extras.
  `ClqV02EndToEndWorkflowTests` — the committed workflow end-to-end → checklist (3 rows: K7/K8/K9, a Fatal).

## Relationship to v01 and the core pattern

v01 is **frozen-legacy bespoke** (own loader/checks/`InternalClqQuestion`/`ParsedSections`; columns M/N).
v02 is the **first core citizen**. New *fitting* validators follow the same shape — a per-version record
(`: IAlignmentIdentity`), a config (`: IClqBaselineConfig`), a `…Profile.Build` composing the baseline checks
plus extension primitives, and a thin task. Adding a within-year input column is the cheap path — see the
**auditor-change-runbook** skill and CLAUDE.md "Implementing an auditor-mandated column change". A
**cross-year-relevant** change (new identity key, a different matching basis) is out of scope → duplicate-and-defer.
