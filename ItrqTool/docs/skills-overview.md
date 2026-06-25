# Skills Overview

An overview of the on-demand skills defined under `.claude/skills/`. Each skill's `name` +
`description` is loaded at startup; the body loads only when judged relevant or when a prompt
names the skill explicitly. Sheet-specific specs and infrequent task guides live here, keeping
`CLAUDE.md` to always-on governance.

Generated: 2026-06-15; relocated to `docs/` and updated 2026-06-17 (added `clq-validation-v02`, `auditor-change-runbook`); updated 2026-06-18 (added `presentation-conventions`, extracted from CLAUDE.md to keep it under the 40k always-on limit); updated 2026-06-25 (added `rlq-validation`).

| Skill | Folder | One-line purpose |
|---|---|---|
| diff-task-conventions | `diff-task-conventions/` | Cross-sheet machinery shared by all questionnaire diff tasks |
| clq-diff | `clq-diff/` | Control Level Questions diff task |
| rlq-diff | `rlq-diff/` | Risk Level Questions diff task |
| gd-diff | `gd-diff/` | General Data diff task (multi-row questions) |
| cell-range-diff | `cell-range-diff/` | General-purpose address-by-address cell-range diff |
| deployment | `deployment/` | Publishing flow and deployed-install runtime paths |
| clq-validation | `clq-validation/` | CLQ_v01 validation task (within-year and cross-year checks, 18 findings) |
| clq-validation-v02 | `clq-validation-v02/` | CLQ_v02 validation task on the generic core (18 baseline + 3 stability findings) |
| rlq-validation | `rlq-validation/` | RLQ validation tasks (v01/v02) — multi-row bespoke parser via RunFromParsedGated; 14/17 findings |
| auditor-change-runbook | `auditor-change-runbook/` | Runbook for an auditor-mandated questionnaire change (add/remove/move a within-year input column) on a core validator |
| presentation-conventions | `presentation-conventions/` | WPF/MVVM Presentation-layer reference (view models, UI-model records, shell/navigation, composition root) |

---

## diff-task-conventions

**Scope:** Cross-sheet machinery shared by every questionnaire diff task (CLQ, RLQ, General
Data). The "load when building or modifying any diff task" foundation that the three per-sheet
skills defer to.

**Summary:** Documents the shared **matching matrix**: base similarity computed on `QuestionText`
only, plus contextual bonuses (+0.10 same section, +0.10 same number), Hungarian assignment for
optimal one-to-one matching, and a 0.5 match threshold (below it, questions fall to Added/Removed).
States the locked **base-vs-adjusted similarity invariant** (lesson 11): the reported
`SimilarityScore`/`SecondBestSimilarity` is always the *base* score — bonuses disambiguate matching
only and never leak into the user-visible number. Defines **DV (data validation) and CF (conditional
formatting) capture, display, and comparison** rules read via `IExcelStructureReader`
(`DataValidationType/Formula/Operator/Formula2`, `ConditionalFormattingType/Operator/Value/Value2`,
with documented nulling rules — e.g. `DataValidationOperator` null for List/Custom/AnyValue).
Display strings are computed in the task mapping layer via `DvDisplayFormatter.FormatFull` and
`CfDisplayFormatter.Format` (Excel-words rendering, "—" when none). Comparison is
**detect-everything with no muting** via `DvComparer.IsDvChangedFull` / `CfComparer.IsCfChanged`
(the former List-CF mute has been removed). Inventories the `src/ItrqTool.Tasks/Shared/` helpers
(extracted only after proving the per-sheet copies byte-identical) and explains the
**duplicate-and-defer** philosophy: engines, parsers, Hungarian algorithm, `TextSimilarity`, and
HTML scaffold stay duplicated per-sibling until proven convergent. Also covers the shared
**sheet-order "Current sheet"/"Previous sheet" tabs** (derived in JS from existing arrays). The
**abstraction-extraction trigger** is the 4th sheet (Risk Level Exposure).

---

## clq-diff

**Scope:** Control Level Questions sheet diff — `ControlLevelQuestionDiffTask`, TaskType
`"ControlLevelQuestionDiff"`, namespace `ItrqTool.Tasks.ControlLevelQuestionDiff`.

**Summary:** Compares the Control Level Questions sheet between two reference years and produces an
interactive HTML diff report. Takes **four task parameters** (previous/current workbook full
filenames + previous/current config JSON full filenames); each config is deserialized independently
and applied only to its own workbook, so the two workbooks may differ structurally across audit
years. Documents the **`AuditQuestion`** record (carries parsed question + captured DV/CF fields)
and its static prefix helpers — `ExtractNumber` returns the leading number token
(`"1.2) text" → "1.2"`, null if none) and `StripPrefix` removes it (`→ "text"`); `QuestionText`
holds stripped text, `OriginalText` the raw cell. A matched pair is **Changed or Unchanged only —
no separate ValidationChange category**; `ChangedQuestion` carries `TextChanged` (true when
similarity < 1.0), `NumberChanged`, `DvChanged`, `CfChanged` (no muting). Describes
**`ControlLevelQuestionsConfig`** (defaults: SheetName `"Control Level Questions"`, TextColumn
`"C"`, InputColumn `"D"`, plus `ChapterRows`, `SectionRows`, computed `ParsedSections`) and the
**`sectionRows` format** `"<sectionRow>:<firstQuestionRow>-<lastQuestionRow>"` (1-based; sectionRow
positive, first > sectionRow, last ≥ first; invalid entries throw `FormatException`, caught →
`Succeeded:false`), with an example config JSON. Shares `HtmlQuestionDiffReportWriter` with RLQ (same
tabs); the CLQ report has no explanation diff block.

---

## rlq-diff

**Scope:** Risk Level Questions sheet diff — `RiskLevelQuestionDiffTask`, TaskType
`"RiskLevelQuestionDiff"`, namespace `ItrqTool.Tasks.RiskLevelQuestionDiff`. The second diff task.

**Summary:** Structurally similar to CLQ but with deliberate divergences. The **`RiskLevelQuestion`**
record has **no `ChapterName`** (RLQ has sections only) and **no `OriginalText`** (the number sits in
its own column B, so there is no prefix to strip); it carries the same DV/CF fields as the other
sheets. **`RiskLevelQuestionsConfig`** uses dedicated columns — `NumberColumn` (default `"B"`),
`TextColumn` (`"C"`), `AnswerColumn` (`"D"`), `ExplanationColumn` (`"E"`), `SectionRows`,
`ParsedSections` — and has **no `ChapterRows`**; `sectionRows` uses the same format as CLQ. Parser
reads the number directly from column B (no regex, trimmed, null if blank), text from C (no strip),
explanation from E, DV/CF from column D, section name from C at the section row. The diff engine
mirrors CLQ but adds an **`ExplanationChanged`** flag (computed post-match from a separate
`TextSimilarity.Score` on the matched pair's explanations). **Locked design decision:**
`ExplanationPrompt` does **not** participate in the matching matrix (keeps "reported similarity is
base question-text similarity" clean); an `ExplanationBonus` is a reserved one-line change if needed.
Takes **five parameters** (the four CLQ-style paths + optional `reportTitle`). Shares
`HtmlQuestionDiffReportWriter` with CLQ, adding a per-changed-question **explanation diff block**.

---

## gd-diff

**Scope:** General Data sheet diff — `GeneralDataDiffTask`, TaskType `"GeneralDataDiff"`, namespace
`ItrqTool.Tasks.GeneralDataDiff`. The third diff task.

**Summary:** Unlike CLQ/RLQ (one question per sheet row), a **General Data question spans multiple
sheet rows** with answer template-label cells across columns D/E/F per row and an explanation cell
in column G (defaults: B=number, C=text/section header, D/E/F=answer labels, G=explanation). Matching
mirrors the RLQ engine (QuestionText-only base, +0.10 section / +0.10 number bonuses, threshold 0.5).
Introduces **per-cell diffing** (novel vs CLQ/RLQ): answer cells keyed by `(RowOffset, Column)`,
explanation cells by `RowOffset`; a `ChangedQuestion` may flag `AnswerCellsChanged` and/or
`ExplanationCellsChanged` independently of `TextChanged`/`NumberChanged`. Documents the
**structure JSON** with its `sectionRows` **optional-rowspan shorthand**
`"<sectionRow>:<startRow>(<rowspan>), ..."` where rowspan is inclusive and defaults to 1 (so `18(3)`
spans 18–20, `21` ≡ `21(1)`). Covers `GeneralDataConfig` / `GeneralDataQuestionParser`, the
**cell-inclusion rule** (a D/E/F or G cell joins iff its trimmed `TextValue` is non-empty — DV/CF on
empty cells does not trigger inclusion; question text from column C of the first row only), the full
record family (`AnswerCellChange`/`ExplanationCellChange` carrying old+new raw DV/CF;
`AnswerCellChange` has a `Column`, explanation does not), and the diff engine's post-match
`DiffAnswerCells`/`DiffExplanationCells` (cell present on one side only → add/remove with null
old/new text). Uses its **own parallel writer** `HtmlGeneralDataDiffReportWriter` (own CSS/JS
scaffold, duplicate-and-defer; six tabs). Five parameters, same shape as CLQ/RLQ. **Architectural
note:** GD exposes a *standalone* public parser (the "third-sibling trigger did not fire" — its
multi-row model differs too much to safely share parser code; re-evaluate at the 4th sheet).

---

## cell-range-diff

**Scope:** The general-purpose, parameter-driven diff task — `CellRangeDiffTask`, TaskType
`"CellRangeDiff"`. The counterpart to the three special-purpose per-sheet tasks.

**Summary:** Compares two workbooks **cell-by-cell at the same address** over configured A1 ranges —
**no fuzzy matching, no sections, no question concept**; an address is simply changed or unchanged (a
blank counts as a value). For structurally simple sheets where a special-purpose implementation isn't
warranted; one class serves many such sheets. **No config file** — all settings are inline
workflow-JSON node parameters: `file1Path`/`file2Path` (baseline/comparison), `sheet1Name`/
`sheet2Name` (may differ), `ranges` (semicolon-delimited A1, e.g. `"B2:F40;H2:H40"`), `compareScope`
(`"Value"` or `"ValueAndDvCf"`, **required, no default**), optional `reportTitle`; output HTML from
`outputs.report`. Comparison: addresses are the union over declared ranges in row-major order; **value**
is cell text compared after `Trim()` (a documented whitespace relaxation); **DV + CF** compared only
when `compareScope == ValueAndDvCf` via the Shared comparers/formatters. Reads via
`IExcelStructureReader.ReadCells(file, sheet, ranges)` — the address-driven read that (unlike
`ReadRows`) returns every address including blank cells and cells carrying only DV/CF, making DV/CF
comparison trustworthy on data-entry cells. **Strict by construction:** missing/blank required params,
absent/unrecognised compareScope, empty or malformed ranges, missing files, and missing sheets each
return `Succeeded:false` with an Error (never a silent default); `OperationCanceledException` always
propagates. Report via `HtmlCellRangeDiffReportWriter` (Changed + Unchanged tables; Address | File 1 |
File 2, plus the four DV/CF columns only under ValueAndDvCf). Notes a **parked "*" whole-worksheet
wildcard** enhancement and its implementation caveats.

---

## clq-validation

**Scope:** CLQ_v01 validation task — `ControlLevelQuestionValidationV01Task`, TaskType
`"ControlLevelQuestionValidation_v01"`, namespace `ItrqTool.Tasks`.

**Summary:** Validates a current-year Control-Level-Question response against the empty auditor
template (within-year) and the previous-year response (cross-year), emitting a `ValidationReport`
JSON consumed by `FeedbackChecklistAssembler`. **Task contract:** parameter
`configurationFullFilename`; inputs `currentResponse` / `emptyTemplate` / `previousResponse`;
output `report`. `Succeeded:false` only on unreadable files or invalid config — a Fatal finding is
data, not a failure. **Config** (`ClqV01Config`): one config governs all
three workbooks; carries `SheetName`, eight column letters (D=text, E=guidance, F=previousAnswer,
H=answer, I=strengths, J=weaknesses, M=providedBy, N=xrefId), `ChapterRows`, `SectionRows`
(`"<sectionRow>:<first>-<last>"` format), `AllowedAnswers`, `DeviationThreshold`, and
`SeverityOverrides` (per finding-id severity overrides; unknown keys rejected). Loader is strict
and fail-loud (`UnmappedMemberHandling.Disallow`; all errors collected before throwing). Documents
the **18 findings** (from `ClqBaselineFindings.All`, shared across CLQ versions) grouped as: malformed keys
(`XrefIdEmptyOrDuplicated` Fatal); within-year structure (`QuestionRemoved`/`Added`/`RowShifted`
Error, `NumberFormatUnrecognized` Warning); within-year frozen values
(`ReferenceTextAltered`/`PreviousAnswerAltered` Warning); frozen constraint
(`AnswerValidationRuleChanged` Error); input validity (`AnswerMissing` Error,
`AnswerNotInAllowedSet` Fatal, `StrengthsMissing`/`WeaknessesMissing` Error); cross-year identity
(six outcomes: `XrefIdConflict` Error, `NewXrefIdResemblesPrevious`/`SameXrefIdTextDiverged`
Warning, `NoPreviousBaseline` Information, `AnswerDeviation` Warning,
`PreviousAnswerUnusable` Information). Key **gating rules**: `PreviousAnswerAltered` (F-integrity)
runs on **every Agree row** regardless of current-answer usability; strengths/weaknesses matrix and
deviation are gated on a **usable current answer** (∈ AllowedAnswers); deviation additionally gated
on previous answer being int-parseable ∈ AllowedAnswers. `DvPatcher` re-reads DV on the
answer column via `ReadCells` (address-driven, one DvRole: answer → H) after parsing, so
blank-but-DV'd template H cells are captured. Covers the **file-source convention** (`StaticFileSource` now / `DynamicFileSource`
later — same `output` contract, pure rewiring, zero task changes) and the **workflow topology**
(3× file-source → validate → assemble; assembler output is `.xlsx` via
`ClosedXmlFeedbackChecklistWriter`, NOT HTML). **Integration trial:** 193 real audit question
texts (distinct, unambiguous), 23 seeded findings (10 local perturbations + 6 structural rows),
exact-set test + end-to-end workflow test; trial artifacts at `trial-output/clq-v01/`.

---

## clq-validation-v02

**Scope:** CLQ_v02 validation task — `ControlLevelQuestionValidationV02Task`, TaskType
`"ControlLevelQuestionValidation_v02"`, namespace `ItrqTool.Tasks`. First production consumer of the
version-neutral `ItrqTool.Tasks.QuestionnaireValidation` core (v01 also runs on the core, column map M/N, no K).

**Summary:** Validates a current-year CLQ response against the empty template (within-year) and the
previous-year response (cross-year), emitting a `ValidationReport` consumed by `FeedbackChecklistAssembler`.
Built entirely on the generic core: `ValidationPipeline.Run<T>` wires read → parse (profile `RecordFactory`)
→ `DvPatcher` (per DV-role) → `AlignmentEngine.Align<T>` → `ClqBaselineChecks` (the 18-finding CLQ baseline,
a one-time copy of v01's catalogue, shared across CLQ versions) + extension primitives. Two-phase config
validation (structural at `ConfigLoader.Load`; override-keys in `ValidationPipeline.Run`). The **column map**
inserts answer-stability at **K**, shifting provided-by to **N** and xref-id to **O** (vs v01's M/N).
`ControlLevelQuestionValidationV02Config : IClqBaselineConfig` carries 9 columns + `SheetName` +
`ChapterRows` (string, not int) + `SectionRows` + `AllowedAnswers` + `AllowedStabilityAnswers` +
`DeviationThreshold` + `SeverityOverrides`; **no `ParsedSections`** (sections derive at RUN via `LayoutParser`).
`ClqV02Profile.Build` declares 2 DV-roles (answer H, stability K) and 2 extension primitives
(`RequiredInputCell` + `FrozenConstraintCell`, role `answer-stability`). Adds **3 findings**:
`input-cell.answer-stability.missing` (Error, unconditional per row), `input-cell.answer-stability.not-in-allowed-set`
(Fatal), `constraint.answer-stability.validation-rule-changed` (Error), all at `K{row}`. Production asset
`configs/clq-v02-validation-config.json` (pinned by `ClqV02ProductionConfigAssetTests`, incl. a 30-section
parse). Five-node trial workflow `clq-v02-validation-trial.json`. Synthetic trial under
`tests/ItrqTool.Integration.Tests/ClqV02/`: `ClqV02BaselineFactory` (reuses the 193-question corpus,
baseline-zero), `ClqV02WorkbookWriter` (K/N/O + a Yes/No K-DV), `ClqV02BaselineTests` (zero findings),
`ClqV02StabilityPerturbationTests` (3-finding exact-set), `ClqV02EndToEndWorkflowTests` (full workflow →
checklist). New *fitting* validators follow the same per-version profile-on-core pattern; cross-year-relevant
changes fall back to duplicate-and-defer.

---

## rlq-validation

**Scope:** RLQ validation tasks — `RiskLevelQuestionValidationV01Task` (TaskType
`"RiskLevelQuestionValidation_v01"`) and `RiskLevelQuestionValidationV02Task` (TaskType
`"RiskLevelQuestionValidation_v02"`), namespace `ItrqTool.Tasks`. Both run on the version-neutral
`QuestionnaireValidation` core but with an RLQ-specific multi-row parser.

**Summary:** Validate a current-year Risk-Level-Question response against the empty template (within-year)
and the previous-year response (cross-year), emitting a `ValidationReport` consumed by
`FeedbackChecklistAssembler`. RLQ questions span **one or more contiguous rows**, so RLQ does NOT use the
shared single-row `QuestionParser`: each version has a **bespoke `RlqV0xQuestionParser`** (grouping key =
XrefId, repeated per row; once-per-question merged cells read from the top row; the I/J/K explanation triplet
per-row) and the task runs **`ValidationPipeline.RunFromParsedGated`** (NOT `Run`/`RunFromParsed`) with the
identity-integrity gate (`HaltOnMalformedKeys`, `MalformedKeyCheck` on the XrefId column — a blank/duplicate
key yields only the gate's Fatal `structure.xrefid-empty-or-duplicated` + `Halted=true`). **Sections only,
no chapters.** **Column maps:** v01 C/D/E/F/G/H/I/J/K/L + ProvidedBy **O** / XrefId **Q**; v02 same C–L +
HowExplanation **M** + ProvidedBy **P** / XrefId **R**. **DvRoles:** answer → **H**, material-change → **L**
(inline DVs resolved in the profile, range-ref/named-range via a `DvRangeRefResolver` post-pass).
`DeviationThreshold` is **relative (a fraction**, e.g. 0.25 = 25 %). **Finding catalogue: 14 (v01) / 17
(v02)** — pinned by `RlqV02ProfileCatalogueTests`. v02 adds three ids: **Rule 1**
`ConditionalRequirement` on column **M** (required when **L** material-change ∈
`MaterialChangeExplanationTriggers`) → `…conditionally-required-missing` (Error); **Rule 2**
`ConfiguredTriggerInDvList` on **L** → `config.material-change-explanation.trigger-not-in-dv-list` /
`…dv-list-unresolvable` (both ConfigConsistency, **Fatal**). Note `input-cell.{role}.dv-vocabulary-unresolvable`
is an *existing baseline* id (DvConformanceCell, Error), not a new-v02 surface. Five-node trial workflows
(`workflows/rlq-v0x-validation-trial.json`: StaticFileSource×3 → validate → `FeedbackChecklistAssembler`) and
exact-set perturbation + end-to-end integration trials under `tests/ItrqTool.Integration.Tests/RlqV01|RlqV02/`.
Includes a pointer to the **VCP-frozen** `RiskLevelQuestionInject_v01_to_v02` injector (reference-only, no
carry-forward; H→G typed, K→J, O→P; M not injected).

---

## auditor-change-runbook

**Scope:** On-demand runbook for implementing an auditor-mandated questionnaire change on a core-based
validator (`clq-validation-v02` and later). Not a task skill — a procedural guide for developers.

**Summary:** Leads with the **scope gate** — a *within-year* input-cell change (a new/removed/moved
column judged against the empty template and the previous-year value within the current structure) is
the cheap, supported path; a *cross-year-relevant* change (question identity, the matching basis, or
the corpus shape) is out of scope and must **duplicate-and-defer** to a new versioned stack. Walks the
full surface a within-year column change touches: config JSON + config record/`Validate()`, the
per-version question record + its DV fields, the profile (`RecordFactory`, `DvRole`s, and the
`RequiredInputCell` / `FrozenConstraintCell` extension primitives), the trial workbook writer + baseline
factory, the asset/baseline/perturbation/end-to-end tests, and the docs/baseline bump. The **add-column**
path is DEMONSTRATED via the v02 "add column K" worked example; **remove** and **move/alter** are
INFERRED. Cross-references the CLAUDE.md "Implementing an auditor-mandated column change" checklist, the
`clq-validation-v02` skill, and the design ADR (`docs/design/validation-core-and-clq-v02.md`).

---

## presentation-conventions

**Scope:** WPF/MVVM Presentation-layer reference. Load when working on any view model, XAML view,
UI-model record, navigation, or the run-view log/result display. Extracted verbatim from CLAUDE.md
on 2026-06-18 to keep the always-on file under the 40k-char limit; the load-bearing rules (rule-5
bindable boundary, `AddItrqToolServices` as the single source of truth for the object graph) remain
summarised in CLAUDE.md.

**Summary:** Establishes the framework/pattern conventions — **WPF on .NET 10** (`net10.0-windows`),
**MVVM via CommunityToolkit.Mvvm source generators** (`[ObservableProperty]`, `[RelayCommand]`),
ViewModels in `ItrqTool.Presentation/ViewModels/`, Views (XAML) in `…/Views/`. Documents the
**UI-model surrogate records** in `src/ItrqTool.Presentation/UIModels/` (`WorkflowListItem`,
`WorkflowGroupItem`, `WorkflowLoadFailureItem`, `TaskRowItem` + `TaskRowStatus`, `TaskParameterItem`,
`LogEntry`) that keep Domain types off the bindable surface (non-negotiable rule 5). Details
**`WorkflowRunViewModel` responsibilities:** the `WorkflowSession` lifecycle, per-row `TaskRowItem`
**status derivation** from `session.CurrentIndex`/`Status`, `RunTaskCommand` flow, the `SelectedTask`
**configuration viewer** (not a result panel) projecting node parameters as `TaskParameterItem` rows,
and the **result-display-to-log translation** (private `AppendResultToLog` maps `TaskResult`/
`MessageSeverity` into `LogEntry` rows pushed to `IUiLogSink` — the boundary enforcing rule 5).
Covers `RunButtonLabel`/`CanRun`/`BackCommand`/`OpenWorkingFolderCommand` behaviour, the
**`AddItrqToolServices(workflowsDirectoryPath, workflowDataRoot)`** composition-root entry point as
the single source of truth for the production object graph, the **event-based shell/navigation**
pattern (`ShellViewModel.CurrentViewModel` + `MainWindow` DataTemplates; `WorkflowSelected`/
`BackRequested` events; no messenger/navigation service), and **`WorkflowListViewModel`** load-failure
banner behaviour (`Failures`, `ShowFailureDetails`, `ToggleFailureDetailsCommand`).

---

## deployment

**Scope:** Publishing and runtime paths. Load when publishing a release, editing `publish.ps1`, or
reasoning about deployed-install file locations.

**Summary:** The app ships as a **framework-dependent, single-file win-x64 executable** (target
machines need Microsoft.WindowsDesktop.App 10.0.x). Documents the canonical publish configuration and
*why* each flag is set: net10.0-windows / win-x64, self-contained `false`, `PublishSingleFile true`,
`EnableCompressionInSingleFile false` (compression needs self-contained — **NETSDK1176**),
`PublishReadyToRun true`, `DebugType embedded` (PDB in the exe), `PublishTrimmed false` (WPF, Scrutor
reflection, System.Text.Json reflection, and CommunityToolkit.Mvvm source generators are unsafe to
trim). Describes the **`publish/` output layout** (`ItrqTool.exe`, `appsettings.json`, an empty
`workflows/`, a generated `README.txt`; the directory is gitignored, zipped, and handed to the user
to extract and run — no installer, no admin rights). **Publishing is driven by `publish.ps1`** at the
repo root (invokes `dotnet publish` with canonical flags, stages `appsettings.json`, wipes published
`workflows/` clean, writes the README) — **never invoke `dotnet publish` manually for a release**.
**Runtime paths:** `AppContext.BaseDirectory` is the exe's directory (where it finds `appsettings.json`
and `workflows/`); working data and logs default to `%USERPROFILE%\Documents\ItrqTool` and
`…\ItrqTool\logs`, configurable in `appsettings.json` and persisting across installs/upgrades.
