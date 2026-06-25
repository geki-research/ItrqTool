---
name: rlq-validation
description: RLQ validation tasks — RiskLevelQuestionValidationV01Task (TaskType "RiskLevelQuestionValidation_v01") and RiskLevelQuestionValidationV02Task (TaskType "RiskLevelQuestionValidation_v02"), both on the version-neutral QuestionnaireValidation core but with a BESPOKE multi-row parser (XrefId-grouped, merged once-per-question cells) run via ValidationPipeline.RunFromParsedGated (identity-integrity gate, HaltOnMalformedKeys). Within-year checks (structure, frozen constraints, input presence, DV conformance) plus cross-year answer deviation and per-row explanation completeness. Finding catalogue is 14 (v01) / 17 (v02); v02 adds Rule 1 ConditionalRequirement on column M (trigger = L material-change) and Rule 2 ConfiguredTriggerInDvList on L. Column maps: v01 C/D/E/F/G/H/I/J/K/L + ProvidedBy O / XrefId Q; v02 same C–L + HowExplanation M + ProvidedBy P / XrefId R. DvRoles answer→H, material-change→L. Sections only, no chapters; relative (fraction) DeviationThreshold. Load when working on the RLQ validation tasks, their configs, the multi-row parser, the finding catalogue, the two v02 checks, or the trial scenarios. See the RLQ inject pointer section for the VCP-frozen v01→v02 injector.
---

# RLQ — Risk-Level-Question validation (v01 + v02)

`RiskLevelQuestionValidationV01Task` (TaskType `"RiskLevelQuestionValidation_v01"`) and
`RiskLevelQuestionValidationV02Task` (TaskType `"RiskLevelQuestionValidation_v02"`, both namespace
`ItrqTool.Tasks`) validate a current-year Risk-Level-Question response against the empty auditor template
(within-year) and the previous-year response (cross-year), emitting a `ValidationReport` JSON consumed by
`FeedbackChecklistAssembler`. Both run on the version-neutral `ItrqTool.Tasks.QuestionnaireValidation` core
(the same core as the CLQ validators — see the `clq-validation` / `clq-validation-v02` skills) but with two
RLQ-specific divergences:

- **Multi-row questions.** An RLQ question spans one OR MORE contiguous sheet rows, so RLQ does NOT use the
  shared single-row `QuestionParser`. Each version has a bespoke `RlqV0xQuestionParser` and the task runs
  the IO-free align-and-check half via **`ValidationPipeline.RunFromParsedGated`** (NOT `Run` /
  `RunFromParsed`) after its own read → parse → DV-patch.
- **Identity-integrity gate.** RLQ opts into the gate: `HaltOnMalformedKeys: true` with
  `IdentityGateCheck = new MalformedKeyCheck<…>(config.XrefIdColumn)`. A blank/duplicate XrefId in any
  workbook yields ONLY the gate's Fatal finding plus `Halted=true`, before any other check runs.

Sections only — RLQ has no chapters. The findings are wired through the core's declarative extension
primitives; there is no `ClqBaselineChecks` call (the profile's `RunBaseline` returns an empty list).

## Task contract

| | |
|---|---|
| **TaskType** | `RiskLevelQuestionValidation_v01` / `RiskLevelQuestionValidation_v02` |
| **Parameter** | `configurationFullFilename` — path to the RLQ config JSON (full path — used verbatim, not CWD/`AppContext.BaseDirectory`-relative) |
| **Input** `currentResponse` | Current-year response workbook |
| **Input** `emptyTemplate` | Empty auditor template workbook |
| **Input** `previousResponse` | Previous-year response workbook |
| **Output** `report` | `ValidationReport` JSON (via `ValidationReportSerializer`) |

`Succeeded:false` ONLY for an unreadable/missing file or an invalid/under-specified config (including a
malformed `SectionRows` range — `LayoutParser.Parse` runs inside `…Profile.Build` and throws
`FormatException`, caught → `Succeeded:false`). A finding — including a `Fatal` — is data in the report; it
does NOT fail the task. Ctor injects `IExcelStructureReader` + `ILogger<…>`. On a gate halt the report
carries `Halted=true`; on a normal run the serializer omits the key (`bool?` null) so the report stays
byte-identical.

## Config (`RlqV01Config` / `RlqV02Config`)

One config governs all three workbooks (shared format + sheet name). Both configs are strict/fail-loud via
the generic `ConfigLoader.Load<…>(json, c => c.Validate())` (NOT a per-version loader): blank/invalid column
letters fail, non-distinct column letters fail, empty `SectionRows` fails, negative `DeviationThreshold`
fails. Like the CLQ configs there is **no `ParsedSections`** — `SectionRows` parses at RUN via `LayoutParser`.

**Column maps (source-pinned — the letters are version-specific):**

| role | v01 | v02 |
|---|---|---|
| QuestionNumber | C | C |
| Text (dual: section-name + question-text) | D | D |
| Guidance | E | E |
| RequestedType | F | F |
| PreviousAnswer | G | G |
| Answer | H | H |
| RequestedExplanation | I | I |
| PreviousExplanation | J | J |
| CurrentExplanation | K | K |
| MaterialChange | L | L |
| **HowExplanation** (new in v02) | — | **M** |
| ProvidedBy | **O** | **P** |
| XrefId | **Q** | **R** |

`DeviationThreshold` is a **relative (fractional) cross-year threshold** — e.g. `0.25` = 25 %: a
confidently-matched answer whose value moved by `|cur − prev| / |prev|` ≥ the fraction is flagged
(WholeNumber/Decimal answers only; `prev == 0` skipped). v02 additionally carries
`MaterialChangeExplanationTriggers` (the MaterialChange values that make column M conditionally required —
empty `[]` is invalid). `SeverityOverrides` (per finding-id) is validated later, inside the pipeline.

### Production configs

- **`configs/rlq-v01-validation-config.json`** — Sheet `"IT Risk Level Questions"`; columns
  C/D/E/F/G/H/I/J/K/L + ProvidedBy **O** + XrefId **Q**; `SectionRows
  ["3:4-21","22:23-42","43:44-58","59:60-71"]`; `DeviationThreshold 0.25`; `SeverityOverrides {}`. Pinned by
  `RlqV01ProductionConfigAssetTests`.
- **`configs/rlq-v02-validation-config.json`** — same C–L, **plus** HowExplanation **M**, ProvidedBy **P**,
  XrefId **R**; `MaterialChangeExplanationTriggers ["Yes"]`; `DeviationThreshold 0.25`; `SeverityOverrides
  {}`. Pinned by `RlqV02ProductionConfigAssetTests`.

## The multi-row parser

`RlqV01QuestionParser.Parse` / `RlqV02QuestionParser.Parse` (static; signature `(rows, layout, config,
messages)`) are **bespoke multi-row** parsers:

- **Grouping key = XrefId** (col Q for v01, col R for v02), repeated identically on every row of a question.
  A maximal run of contiguous rows with the same non-blank XrefId is one question.
- **Once-per-question fields are merged cells** — their value sits on the group's FIRST (top) row and reads
  blank on continuation rows, so they are read from the first row.
- **The explanation triplet (I / J / K)** is per-row and accumulated across the group in row order.
- **Sections only.** Section-header rows come from the `QuestionnaireLayout` (built by
  `LayoutParser.Parse` with empty `chapterRows`); the section name is read from each header's name column
  (column D for RLQ). Rows outside any section's question-row extent are skipped, consistent with
  `QuestionParser`.

The v02 parser delegates `RlqExplanationRow` from the v01 namespace (shared, not cloned).

## DV patching + range-ref resolution

Both profiles declare **two `DvRoles`: answer → H and material-change → L**. After parsing, the task patches
each role's four DV fields via `DvPatcher.Patch` (address-driven `IExcelStructureReader.ReadCells`, so
blank-but-DV'd cells are captured). The profile's `ApplyDv` resolves **inline** List DVs into
`…DvListValues`; a post-patch **`DvRangeRefResolver.Resolve`** pass then resolves **range-ref / named-range**
List DVs for both H and L (without it, a non-inline L DV stays null and Rule 2's `dv-list-unresolvable`
Fatal would fire spuriously). This is what lets the DV-conformance and Rule-2 checks see the resolved
controlled vocabulary.

## Findings (v01: 14 / v02: 17)

Authority for the counts and the exact-set: `RlqV02ProfileCatalogueTests` (v01 = 14 ids, v02 = 14 + 3 = 17;
v02 ⊇ v01; exactly three new ids). Each finding is identified by a finding-id string; default severities are
config-overridable via `SeverityOverrides`.

**Baseline / shared (the 14 v01 ids — all carried into v02 unchanged):**

| # | Finding id | Check | Default | Emitting primitive |
|---|---|---|---|---|
| 1 | `structure.xrefid-empty-or-duplicated` | Structure | **Fatal** | `MalformedKeyCheck` (identity gate) |
| 2 | `input-cell.material-change.missing` | MissingResponse | Error | `RequiredInputCellAnyValue` (material-change) |
| 3 | `input-cell.answer.missing` | MissingResponse | Error | `RequiredInputCellAnyValue` (answer) |
| 4 | `structure.question-removed` | Structure | Error | `WithinYearStructureCheck` |
| 5 | `structure.question-added` | Structure | Error | `WithinYearStructureCheck` |
| 6 | `structure.question-row-shifted` | Structure | Error | `WithinYearStructureCheck` |
| 7 | `constraint.answer-dv.validation-rule-changed` | FrozenConstraint | Error | `FrozenConstraintCell` (answer-dv) |
| 8 | `constraint.material-change-dv.validation-rule-changed` | FrozenConstraint | Error | `FrozenConstraintCell` (material-change-dv) |
| 9 | `input-cell.answer.not-conformant` | InputConformance | Error | `DvConformanceCell` (answer) |
| 10 | `input-cell.answer.dv-vocabulary-unresolvable` | InputConformance | Error | `DvConformanceCell` (answer) |
| 11 | `input-cell.material-change.not-conformant` | InputConformance | Error | `DvConformanceCell` (material-change) |
| 12 | `input-cell.material-change.dv-vocabulary-unresolvable` | InputConformance | Error | `DvConformanceCell` (material-change) |
| 13 | `cross-year.answer-deviation` | Deviation | Warning | `CrossYearDeviationCell` (answer) |
| 14 | `input-cell.explanation.incomplete` | MissingResponse | Error | `ExplanationCompletenessCell` (v01) / `…CellV02` (v02) |

**New in v02 (the three exact-set additions):**

| # | Finding id | Check | Default | Emitting primitive |
|---|---|---|---|---|
| 15 | `input-cell.material-change-explanation.conditionally-required-missing` | ConditionalRequirement | Error | `ConditionalRequirement` (Rule 1) |
| 16 | `config.material-change-explanation.trigger-not-in-dv-list` | ConfigConsistency | **Fatal** | `ConfiguredTriggerInDvList` (Rule 2a) |
| 17 | `config.material-change-explanation.dv-list-unresolvable` | ConfigConsistency | **Fatal** | `ConfiguredTriggerInDvList` (Rule 2b) |

> **Don't confuse `dv-vocabulary-unresolvable` with the new v02 surface.** `input-cell.{role}.dv-vocabulary-unresolvable`
> (#10 / #12) is an **existing baseline** id — emitted by `DvConformanceCell` (InputConformance, default
> **Error**), present in BOTH v01 and v02. The genuinely-new v02 ids are #15–#17, and #16/#17 are
> `config.*` / **ConfigConsistency** / **Fatal** (from `ConfiguredTriggerInDvList`), not `input-cell.*`.

`ExplanationCompletenessCell` (v01, `IExtensionCheck<RlqV01Question>`, wired in `RlqV01Profile`) and
`ExplanationCompletenessCellV02` (a byte-identical v02 clone — only the generic type argument differs; wired
in `RlqV02Profile`) both emit #14 `input-cell.explanation.incomplete` at `K{row}` for every row where the
request (col I) is present but the current explanation (col K) is blank. They skip
`WithinYearJoin.NotEvaluatedMalformedKey` rows. (The two-class duplication is a deliberate
genericize-later — the original implements `IExtensionCheck<RlqV01Question>` and cannot drop into a
`ValidationPipelineProfile<RlqV02Question>`.)

## The two v02 checks — Rule 1 / Rule 2

Both are wired only in `RlqV02Profile` (v01 has neither):

- **Rule 1 — `ConditionalRequirement<RlqV02Question>`.** Column **M** (HowExplanation) is required when
  column **L** (MaterialChange) holds a value in `config.MaterialChangeExplanationTriggers` (`["Yes"]`).
  Selectors: target `q.HowExplanation`, trigger `q.MaterialChange`, provided-by `q.ProvidedBy`. Emits #15
  (`…conditionally-required-missing`, Error) when the trigger holds but M is blank.
- **Rule 2 — `ConfiguredTriggerInDvList<RlqV02Question>`.** On column **L**, verifies that each configured
  trigger value is a member of L's template DV vocabulary (`q.MaterialChangeDvListValues`, populated by the
  range-ref resolver pass). Emits #16 (`trigger-not-in-dv-list`) when a trigger is absent from the list, and
  #17 (`dv-list-unresolvable`) when L's vocabulary could not be resolved at all — both ConfigConsistency,
  **Fatal**.

Note column **M has no data-validation** — no DV-role is declared for it; only H and L carry DV-roles.

## Workflow topology

The two committed validation-trial workflows are 5-node and mirror the CLQ topology:

```
load-current-response  (StaticFileSource → rlq_current_response.xlsx)   ──┐
load-empty-template    (StaticFileSource → rlq_empty_template.xlsx)      ──┤→ validate (RiskLevelQuestionValidation_v0x) → assemble (FeedbackChecklistAssembler)
load-previous-response (StaticFileSource → rlq_previous_response.xlsx)  ──┘
```

`validate` writes `rlq-v0x-validation-report.json` from `configs/rlq-v0x-validation-config.json`; `assemble`
receives `validate.report` on input key `findings01` and produces an **`.xlsx`** feedback checklist via the
shared, version-agnostic `configs/feedback-checklist-assembler-config.json`. Committed workflow files:
`workflows/rlq-v01-validation-trial.json`, `workflows/rlq-v02-validation-trial.json`.

## Trial / worked example

Integration trials live under `tests/ItrqTool.Integration.Tests/RlqV01/` and `…/RlqV02/`:

- **`RlqV01BaselineFactory` / `RlqV02BaselineFactory`** build a fully-consistent **zero-findings** trio from
  the config structure; **`RlqV01WorkbookWriter`** emits the multi-row workbooks (with the answer/material-change
  DVs) for perturbation.
- **v01** proves each finding family as an exact set via per-family `RlqV01*PerturbationTests` (input-presence,
  XrefId-integrity, structure, frozen-constraint, DV-conformance + range-ref/named-range,
  DV-vocabulary-unresolvable, cross-year-deviation, explanation-completeness), plus `RlqV01ReaderParserTests`.
- **v02** adds `RlqV02ConditionalRequirementPerturbationTests` (Rule 1),
  `RlqV02ConfiguredTriggerInDvListPerturbationTests` (Rule 2),
  `RlqV02DvVocabularyUnresolvablePerturbationTests`, and **`RlqV02InheritedFindingSetReproofTests`** (the 14
  baseline ids carried into v02). `RlqV02ProfileCatalogueTests` (in `ItrqTool.Tasks.Tests`) pins the 14/17
  catalogue counts and the three new descriptors' check+severity.
- **`RlqV01EndToEndWorkflowTests` / `RlqV02EndToEndWorkflowTests`** run the committed trial workflows through
  the full engine (all five tasks) and assert the workflow succeeds and the checklist XLSX is produced.

## RLQ inject (v01 → v02) — pointer

The RLQ stack also ships a cross-version injector — task `RiskLevelQuestionInject_v01_to_v02`
(`RiskLevelQuestionInjectV01ToV02Task`). It is **reference-injection only — no carry-forward**. On an
`Agree`-matched question it emits **three** writes into the v02 template:

- **H → G** — the previous answer, written **typed** per the answer type-compatibility policy that keys on
  the **DV-type category** (NOT the native CLR type — ClosedXML surfaces every numeric as `double`): equal
  category → write native (no message); WholeNumber → Decimal **widen** (+Warning); Decimal → WholeNumber
  **narrow**, written as-is/not rounded (+Warning); any other pair → **Error**, skip the G cell, continue;
  blank source → omit. SOURCE category = source-H DV-type, TARGET = v02-H DV-type, read via **two
  `ReadCells`** (`BuildSourceHLookup` returns DV-type + native value; `BuildTargetHLookup` returns DV-type).
- **K → J** — previous current-explanation, per-row position-aligned text (row-count mismatch → write the
  overlap + ONE Warning).
- **O → P** — previous provided-by, text, once at the anchor row.

**Column M (how-explanation) is NOT injected.** Ambiguous outcomes (XrefIdConflict / NewXrefIdWithLookalike /
SameXrefIdTextDiverged) emit ONE Warning and no cells; Neither / NotEvaluatedMalformedKey emit nothing.

Mapper:
`RlqInjectMapper.Map(CrossFormatAlignmentResult<RlqV02Question, RlqV01Question>, RlqV02Config, IReadOnlyDictionary<int,(string? DvType, object? Native)> sourceHByRow, IReadOnlyDictionary<int,string?> targetHByRow) → (cells, messages)`.
Config `configs/rlq-inject-config.json` (`CurrentConfigFilename = rlq-v02-validation-config.json`,
`PreviousConfigFilename = rlq-v01-validation-config.json`). Workflow `workflows/rlq-inject-trial.json`
(StaticFileSource×2 → inject → StaticFileSink; the task performs NO `File.*`/`SaveAs` — placement is the
sink's job). Permanent-guardrail end-to-end test `RlqInjectEndToEndWorkflowTests`.

**VCP-frozen.** Per "Version contract permanence" in CLAUDE.md, the v01→v02 inject pair's read/write outputs
are a permanent contract — implementation may be refactored, but the pair's observable behaviour must not
change.

## Relationship to CLQ + the core pattern

Like CLQ, RLQ runs on the version-neutral `QuestionnaireValidation` core — a per-version record
(`: IAlignmentIdentity`), a config, a `…Profile.Build` composing extension primitives, and a thin task. It
differs in: **multi-row questions** (bespoke `RlqV0xQuestionParser`, not the shared `QuestionParser`); the
task calls **`RunFromParsedGated`** (not `Run` / `RunFromParsed`) with the identity-integrity gate;
**sections only, no chapters**; and findings come entirely from extension primitives (no `ClqBaselineChecks`
baseline). Adding a within-year input column is the cheap path — see the **auditor-change-runbook** skill
(which also covers this multi-row, bespoke-parser-via-`RunFromParsedGated` path). A **cross-year-relevant**
change (new identity key, different matching basis) is out of scope → duplicate-and-defer to a new stack.
