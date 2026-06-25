# DESIGN (AS-BUILT) — Extensible questionnaire-validation core + CLQ_v02 (first consumer)

**Status: IMPLEMENTED — as-built record.** The version-neutral validation core (chunks A–F) and its
first consumer CLQ_v02 (chunks G–H, the I1–I3 synthetic trial, and the J documentation pass) are built
and merged to `main`. This document is a **point-in-time rationale record (ADR)** — the *why* behind the
core, captured at the close of v02. It is **not a living spec**: living governance lives in `CLAUDE.md`
(the "Validation tasks" section + the "Implementing an auditor-mandated column change" checklist) and the
on-demand skills (`clq-validation-v02`, `auditor-change-runbook`). Future changes go *there*, not back
into this doc.

*Originally authored 2026-06-15 as a forward-looking design ("nothing built yet"); reconciled to as-built
on 2026-06-17, with the five reconciliation flags resolved inline and consolidated in §8. Original grounds:
recon 1 (`2026-06-15_113458_clq-v01-recon.md`) + recon 2 (`2026-06-15_120801_clq-recon2-output-genericity.md`).
Detailed signatures are authoritative in `CLAUDE.md` and the recons and are not re-pasted here.*

**Locked inputs (v02):** task `ControlLevelQuestionValidation_v02`; v01 FROZEN, never mutated; new
within-year input column **K = answer stability** ("will the H answer change within 6 months?"), values
**{Yes, No} via a DV dropdown**; three within-year findings (below); **no cross-year stability comparison**;
**strategic direction = build an extensible version-neutral core, v01 frozen-legacy, v02 = first citizen**;
**representation = (i-composed)** — a concrete per-version record + a shared, selector-driven primitive library.

---

## 1. Problem, strategy & scope boundary

**The auditor is unconstrained.** Between audits the auditor may change *anything* in the workbook —
within-year columns, cross-year-relevant structure, the identity/matching scheme, anything. There is no
guarantee a change is "minor." This architecture does **NOT** attempt to absorb arbitrary change; it is a
**targeted optimization for the common *minor* case — within-year input-column changes** — with an explicit
fallback for the rest.

**Why the minor case is tractable.** When a change is within-year-only, the **cross-year spine** (XrefId
join + text matcher + Hungarian + five-outcome reconciliation) and the **output path** (ValidationReport →
checklist) are untouched, so the core can be written once and reused, and a within-year column add/remove
becomes a localized edit. v02's new stability column is exactly this minor case.

**Strategy:** a version-neutral **validation core** (written once), consumed by thin **per-version** units;
shipped versions stay frozen. v01 frozen-legacy (NOT migrated); v02 = first citizen; later *fitting* versions
follow. One accepted one-time cost: copy the *stable* matcher + the *baseline* finding set into the core
(they will not drift). **Cheap-churn target (minor case only):** adding a within-year input column → one
extension primitive + one record field + one parser line + asset value + trial coverage.

**Scope boundary (accepted limitation, NOT a flaw to engineer around):** a **cross-year-relevant change**
(a new/changed identity key, a different matching basis, an altered within-/cross-year join, etc.) may NOT
fit the generic engine. The deliberate fallback is **duplicate-and-defer**: that version forks the core
pieces it needs (copies the engine, writes its own), exactly as v01→core is itself a copy. We do not
pre-build for unknown cross-year changes — we keep the core's pieces copyable so forking stays cheap when it
is unavoidable. Wholesale removal/restructuring of *baseline* questionnaire columns is likewise a deliberate
larger change, not part of the cheap path.

---

## 2. Recon-confirmed foundations (the design rests on these)

- **Output path is version-agnostic** — `ValidationReport(Sheet, TaskType, Findings)` +
  `ValidationReportSerializer` + `FeedbackChecklistAssemblerTask` reference zero CLQ types; the assembler
  maps the 8 `ValidationFinding` fields straight to the checklist and never reads `Check` or a finding-id.
  → **reused as-is**; a version-defined id space flows through untouched.
- **Identity contract = exactly 6 fields** the engine reads: `RowNumber, XrefId, OriginalText,
  QuestionText, SectionName, QuestionNumber`. Engine constructs no question objects internally.
  → `Align<T> where T : IAlignmentIdentity` is a mechanical substitution; only net-new surface =
  generic `AlignedQuestion<T>` / `AlignmentResult<T>` (+ `MalformedKey`) + the enums.
- **Matcher statics are pure primitives** (`TextSimilarity.Score`, `HungarianAlgorithm.SolveMaximumAssignment`)
  → copy verbatim.
- **Finding construction** = look up a descriptor (Id, DefaultEvaluation, Check) → apply
  `SeverityOverrides.GetValueOrDefault(Id, default)`. Catalogue surface needed: `IsValidId` + `Descriptor`.
- **Structure reader** `IExcelStructureReader.{ReadRows, ReadCells}` + `ExcelRowStructure` /
  `ExcelCellStructure` are reused as-is; `ReadCells` is the blank-cell-safe DV read.
- **Legacy types are static/sealed → COPY, never inherit.** v01 untouched.

---

## 3. Target architecture (as-built)

### 3.1 Location
Namespace **`ItrqTool.Tasks.QuestionnaireValidation`** (version-neutral; deliberately not CLQ-specific so a
future RLQ/GD validator could reuse it — but only what CLQ_v02 needs was built). Stays in `ItrqTool.Tasks`
(Tasks → Domain only; uses `IExcelStructureReader`; no ClosedXML/Infrastructure). Sub-areas: `…/Alignment`,
`…/Checks`, `…/Parsing`, `…/Findings`, `…/Config`, plus the pipeline at the root. v01 stays in
`ItrqTool.Tasks.ControlLevelQuestionValidation` (frozen). v02 lives in `ItrqTool.Tasks` (the
`ControlLevelQuestionValidationV02Task`) with its concrete record/config/profile under the v02 area.

### 3.2 Generic alignment (copied from v01 + genericized — Tier-1 reuse)
- `IAlignmentIdentity` — the 6 members (nullability mirrors v01: `int RowNumber`; `string QuestionText`,
  `string OriginalText`, `string SectionName` non-null; `string? XrefId`, `string? QuestionNumber`).
- `TextSimilarity`, `HungarianAlgorithm` — copied verbatim into the core namespace.
- `AlignmentEngine.Align<T>(current, template, previous) where T : IAlignmentIdentity` → `AlignmentResult<T>`
  — the v01 engine, generic. Constants preserved: SectionBonus/NumberBonus 0.10, MatchThreshold 0.50.
- Model: `AlignedQuestion<T>`, `AlignmentResult<T>`, `MalformedKey` (non-generic), enums `WithinYearJoin`,
  `CrossYearOutcome`, `MalformedKeyReason`, `ValidationWorkbook{CurrentResponse, EmptyTemplate, PreviousResponse}`.

### 3.3 Per-version question record (i-composed)
Each version declares a concrete record implementing `IAlignmentIdentity` and carrying its within-year
payload (incl. per-DV-role DV fields) — a **frozen snapshot of that version's format**. For v02:
`ClqV02Question` = v01's 18 fields + `AnswerStability` + the 4 `AnswerStabilityDv*` fields (23 total).

### 3.4 Shared parsing (write-once; per-version supplies only a record factory)
- `QuestionNumberParser` (ExtractNumber / StripPrefix / HasUnsupportedDeeperPrefix) — copy of v01's prefix parser.
- `QuestionParser.Parse<T>(rows, chapterRows, sectionRows, recordFactory, messages)` — owns the shared
  machinery (chapter/section-header tracking, blank-row skip, identity assembly). The per-version
  `recordFactory` reads the version's payload columns and builds the concrete record. A per-version parser ≈
  one factory function.
- `DvPatcher.Patch<T>(…)` — generalizes v01's `PatchAnswerDv`: address-driven `ReadCells` DV read on a
  column, applied per DV-role via a `with`-updater lambda. Called once per DV-role (v02: answer, answer-stability).

### 3.5 Findings — baseline catalogue + extension descriptors
**Layering:** the finding **infrastructure** is sheet-agnostic core; the CLQ **baseline finding set** is a
CLQ specialization (RLQ/GD would bring their own).
- *Sheet-agnostic core* (`…/Findings`): `FindingDescriptor` (Id, DefaultEvaluation, Check, Description),
  `FindingCatalogue` (built FROM whatever descriptors it is handed → `IsValidId(id)` / `Descriptor(id)` /
  `AllIds`), `FindingEmitter` (descriptor + `SeverityOverrides.GetValueOrDefault(Id, default)` →
  `ValidationFinding`).
- *CLQ specialization:* `ClqBaselineFindings.All` = a **one-time copy** of v01's 18 findings
  (ids/DefaultEvaluation/Check), **NOT referencing v01's `ClqFinding` enum** (so v01 stays frozen and future
  ids never touch it). Shared across CLQ versions (v02, v03, …).
- **Extension primitives** contribute role-templated descriptors at profile-build time
  (`input-cell.<role>.missing`, `constraint.<role>.validation-rule-changed`, etc. — role names, never column
  letters).
- A version's `FindingCatalogue` = its baseline descriptors + its profile's extension descriptors. The loader
  validates `SeverityOverrides` keys against it (fail-loud).

### 3.6 Checks — two layers
1. **Baseline checks (role-parameterized; reproduce v01's 18 findings exactly).** One core component
   (`ClqBaselineChecks.Run<T>(AlignmentResult<T>, ClqBaselineRoleMap<T>, IClqBaselineConfig, FindingEmitter)`),
   driven by the profile's role→selector mapping, runs the v01 suite: structure, frozen-value, frozen-constraint,
   input validity, cross-year (five-outcome identity + F-integrity on EVERY Agree + deviation/unusable).
   **Parity stance:** v02's findings for unchanged columns are IDENTICAL to v01's — only the 3 stability findings
   are added. Gating preserved verbatim (F-integrity on every Agree; I/J + deviation gate on a usable answer;
   deviation also gates on previous int-parseable ∈ AllowedAnswers).
2. **Extension primitives (selector-driven, generic over T; for new/added columns).** The shared library:
   - `RequiredInputCell<T>(valueSelector, providedBySelector, role, column, allowed, missingDefault=Error,
     notInSetDefault=Fatal)` → `input-cell.<role>.missing` (blank, **unconditional per row**) /
     `input-cell.<role>.not-in-allowed-set` (present but ∉ allowed). **`allowed` is supplied by the profile FROM
     A CONFIG FIELD (e.g. `config.AllowedStabilityAnswers`) — never a code literal.**
   - `FrozenConstraintCell<T>(…dv selectors…, providedBySelector, role, column, ruleChangedDefault=Error)` →
     `constraint.<role>.validation-rule-changed` (current vs template DV; needs the DvPatcher read for that role).
   - `FrozenValueCell<T>` / `ConditionalRequirement<T>` — DEFERRED (not needed by v02; YAGNI).
     > **[Forward-note 2026-06-25]** `ConditionalRequirement<T>` was subsequently BUILT (BL-020) and WIRED as
     > RLQ-v02 Rule 1 (`input-cell.material-change-explanation.conditionally-required-missing`); see CLAUDE.md
     > "Validation tasks" and the `rlq-validation` skill. `FrozenValueCell<T>` remains deferred (still YAGNI).

**Config-values principle (load-bearing):** a profile composes *which* primitive applies to *which* role (via
type-checked selectors) — it carries **no data values**. All primitive *values* (allowed-sets, thresholds,
column letters) live in the version's **config asset** and are bound into the primitive by the profile from a
config field. Severity *defaults* live in each finding's descriptor (code) and are config-overridable via
`SeverityOverrides`.

### 3.7 Config (per-version concrete; shared loader machinery)
Per-version sealed config carries its column letters + allowed-sets + structural fields. The shared loader is
**`ConfigLoader.Load<TConfig>(json, validate)`** — strict mechanics (`UnmappedMemberHandling.Disallow`,
`JsonStringEnumConverter`, error collection) plus the per-version `validate` delegate. *(As-built: the loader
runs ONLY the structural `validate` delegate; **severity-override-key validation moved to
`ValidationPipeline.Run`** against the assembled `FindingCatalogue` — the **two-phase** validation in §8.)*
v02 config = v01's fields + `AnswerStabilityColumn` + `AllowedStabilityAnswers` (`IReadOnlyList<string>`;
empty `[]` → invalid → fail-loud, mirroring `AllowedAnswers`; the production asset supplies `["Yes","No"]` —
no code-default). *(As-built: v02 config carries **no `ParsedSections`** computed property — see §8 flag 2 —
and `ChapterRows` is `IReadOnlyList<string>`, parsed to int where ordering needs it.)*

### 3.8 Pipeline + task (thin per version)
*(As-built signature:)* **`ValidationPipeline.Run<T>(IExcelStructureReader reader, string currentPath,
string templatePath, string previousPath, ValidationPipelineProfile<T> profile,
IReadOnlyDictionary<string,FindingEvaluation> severityOverrides, ICollection<TaskMessage> messages,
CancellationToken ct) → IReadOnlyList<ValidationFinding>`** — the **profile** carries SheetName / Layout /
RecordFactory / DvRoles / baseline descriptors + RunBaseline / extensions; `severityOverrides` is passed
separately (and its keys validated here against the catalogue). The pipeline: ReadRows ×3 →
`QuestionParser.Parse<T>` ×3 → `DvPatcher.Patch<T>` per DV-role → `AlignmentEngine.Align<T>` → baseline checks +
extension primitives → findings; the **task** wraps them in `ValidationReport` and serializes to the `report`
output. *(This supersedes the design's sketched `Run<T,TConfig>(…, config, profile, …)` — see §8 flag 5.)*
*(Post-v02: the pipeline was later split into `Run` — read + parse + DV-patch — and a new **public**
`RunFromParsed<T>` covering the align-and-check tail, so multi-row validators (e.g. RLQ-v01) can supply their
own parser and call `RunFromParsed` directly rather than going through `QuestionParser.Parse`. See CLAUDE.md
"Validation tasks".)*

The per-version **task** (`ControlLevelQuestionValidationV02Task`, TaskType `ControlLevelQuestionValidation_v02`)
is a thin `IWorkflowTask`: ctor injects `IExcelStructureReader` + logger; resolves the 3 inputs +
`configurationFullFilename` param + loads config via `ConfigLoader.Load`, then calls the pipeline with v02's
profile. Same input keys/param/output as v01 (`currentResponse` / `emptyTemplate` / `previousResponse`; param
`configurationFullFilename`; output `report`). *(As-built: malformed `SectionRows` surface at RUN via
`LayoutParser.Parse` → `FormatException` → task catch → `Succeeded:false`; see §8 flag 2.)*

### 3.9 v01-freeze handling
v01 entirely untouched. The core duplicates the stable matcher + the baseline 18-finding descriptors (one-time,
won't drift). **Equivalence guard (built, chunk A):** a core test runs a v01-shaped fixture through
`AlignmentEngine.Align<T>` and asserts the outcomes v01 is known to produce — proving the generic copy matches
the known-good matcher without touching v01.

---

## 4. CLQ_v02 concrete definition (as-built)

- **Record** `ClqV02Question : IAlignmentIdentity` = v01's 18 fields + `string? AnswerStability` +
  `AnswerStabilityDvType/Formula/Operator/Formula2` (23 total).
- **Profile** `ClqV02Profile.Build(config)`: baseline role→selector + column map (text **D**, guidance **E**,
  previous-answer **F**, answer **H**, strengths **I**, weaknesses **J**, **provided-by N, xref-id O** — the
  K-insertion shift, §6 resolved); DV-roles = {answer→**H**, answer-stability→**K**}; extension primitives =
  `RequiredInputCell(q=>q.AnswerStability, role:"answer-stability", allowed: config.AllowedStabilityAnswers,
  missing:Error, notInSet:Fatal)` + `FrozenConstraintCell(…AnswerStabilityDv…, role:"answer-stability")`.
  (Allowed values come from config, not a literal.)
- **New findings (3):** `input-cell.answer-stability.missing` (Error, MissingResponse, **unconditional every
  current-year question row**); `input-cell.answer-stability.not-in-allowed-set` (Fatal, MissingResponse);
  `constraint.answer-stability.validation-rule-changed` (Error, FrozenConstraint). All at `K{row}`;
  config-overridable via `SeverityOverrides`.
- **Config** `ControlLevelQuestionValidationV02Config : IClqBaselineConfig` (sealed): v01 columns +
  `AnswerStabilityColumn` + `AllowedStabilityAnswers` (both required; empty/blank → fail-loud). The **production
  asset** sets `AnswerStabilityColumn: "K"` and `AllowedStabilityAnswers: ["Yes","No"]`.
- **Asset** `configs/clq-v02-validation-config.json` + `ClqV02ProductionConfigAssetTests` (pins all values +
  `AnswerStabilityColumn=="K"` + `AllowedStabilityAnswers` + a `LayoutParser.Parse(...).Sections.Count == 30`
  assertion — §8 flag 3).
- **Workflow** `workflows/clq-v02-validation-trial.json` (3×StaticFileSource → ControlLevelQuestionValidation_v02
  → FeedbackChecklistAssembler).
- **Trial** under `tests/ItrqTool.Integration.Tests/ClqV02/`: reuses the **193-question corpus**;
  `ClqV02BaselineFactory` (valid K = "Yes" → zero-findings baseline), `ClqV02WorkbookWriter` (writes
  D/E/F/H/I/J/K/N/O + a Yes/No K-DV), `ClqV02BaselineTests` (zero findings), `ClqV02StabilityPerturbationTests`
  (the 3 stability findings as an exact set, rows 7/8/9), `ClqV02EndToEndWorkflowTests` (full workflow →
  checklist). Cross-year scenario carries from v01.

---

## 5. Chunk plan — COMPLETE

All chunks built, reviewed, and merged across several sessions (A–F core; G–H v02 stack; I1–I3 trial; J docs
pass). Each: branch off `main`, build clean + green suite + per-project count delta + scope guard → STOP
committed-but-unmerged → planner review → separate Sonnet GATE-2 byte-identical squash merge → baseline bump as
its own `docs:` commit where the count changed.

| # | Chunk | Status |
|---|---|---|
| A | Generic alignment core + engine tests + equivalence guard | merged |
| B | Shared parser (`QuestionNumberParser`, `QuestionParser.Parse<T>`) + `DvPatcher.Patch<T>` | merged |
| C | Findings infra (`FindingDescriptor`/`FindingCatalogue`/`FindingEmitter`) + `ConfigLoader.Load<TConfig>` | merged |
| D1 | Baseline checks pt 1 (structure / frozen-value / frozen-constraint / input-validity) | merged |
| D2 | Baseline checks pt 2 (cross-year five-outcome + F-integrity + deviation/unusable) | merged |
| E | Extension primitives (`RequiredInputCell`, `FrozenConstraintCell`) | merged |
| F | `ValidationPipeline.Run<T>` | merged |
| G | v02 record + config + profile + task | merged |
| H | v02 asset + asset-test + workflow JSON | merged |
| I1 | v02 trial: spec + baseline factory + writer + baseline-zero test | merged |
| I2 | v02 trial: stability perturbation exact-set tests | merged |
| I3 | v02 trial: end-to-end workflow test | merged |
| J1–J6 | docs pass: asset-test tightening; `clq-validation-v02` skill; CLAUDE.md promotion + checklist; `auditor-change-runbook` skill; skills-overview relocation; this ADR | merged |

---

## 6. Open parameter — RESOLVED

**Template column-shift:** inserting K **shifts** provided-by **M→N** and xref-id **N→O** (confirmed from the
v02 template; reflected in §4's column map, the production asset, and the trial writer). The core (chunks A–G)
was agnostic to this leaf detail; it was resolved before the H asset and the I trial.

---

## 7. Calibration / anti-gold-plating notes

- The auditor is **unconstrained**; this core is a **targeted optimization for the minor (within-year input
  column) case**, NOT a universal framework. **Cross-year-relevant changes are out of scope** → accepted
  fallback is forking the core pieces (duplicate-and-defer), as v01→core is itself a copy. Baseline-column
  removal/restructuring is likewise a deliberate larger change — not pre-built.
- Primitive library limited to the kinds that actually occur (required-input, frozen-constraint; +frozen-value/
  conditional only when a version needs them).
- Composition lives in **code profiles** (type-safe, reviewable), not a JSON rules engine; the config asset
  stays column-letters + allowed-sets + thresholds (v01-shaped).
- The one accepted duplication: the stable matcher + baseline descriptors copied into the core (won't drift;
  proven by the equivalence guard). v01 frozen throughout.

---

## 8. As-built reconciliation (2026-06-17)

v02 is proven end-to-end (zero-findings baseline + the 3 stability findings as an exact set + the full
workflow → checklist). The five flags carried from the implementation handover, resolved:

1. **Column map — RESOLVED.** Inserting K shifted provided-by **M→N** and xref-id **N→O** (the §6 open
   parameter, now closed). §4's profile column map and the production asset read provided-by **N**, xref-id **O**
   (vs v01's M/N).
2. **Section-format-validation timing — DOCUMENTED.** v02 carries **no `ParsedSections`** computed property
   (unlike v01, which validated it at config-LOAD via `ClqConfigException`). v02's `SectionRows` are parsed at
   pipeline-RUN by `LayoutParser.Parse`, which throws `FormatException` on a malformed row → caught by the task →
   `Succeeded:false`. Same user-visible outcome, different stage (run vs load). (Noted in §3.7/§3.8.)
3. **v02 asset-test SectionRows coverage — RESOLVED.** `ClqV02ProductionConfigAssetTests` folds a
   `LayoutParser.Parse(config.ChapterRows, config.SectionRows, …).Sections.Count == 30` assertion into its
   config-load fact (chunk J1), giving v02 the 30-section coverage v01 asserted via `ParsedSections.HaveCount(30)`.
   No test-count change.
4. **v02 task 4-file disk pre-check — PARITY, no action.** v02's pre-execution file-existence block mirrors an
   identical block in v01; parity, not a deviation.
5. **F-series deviations — CARRIED (as-built signatures).** The pipeline is `ValidationPipeline.Run<T>(reader,
   currentPath, templatePath, previousPath, profile, severityOverrides, messages, ct) → findings` — the profile
   carries layout/factory/roles/checks and `severityOverrides` is a separate dictionary — superseding the design's
   sketched `Run<T,TConfig>(…, config, profile, …)`. The task wraps findings in `ValidationReport` and serializes.
   **Config validation is two-phase:** structural errors at `ConfigLoader.Load<TConfig>(json, validate)`; severity-
   override-key errors inside `ValidationPipeline.Run` against the assembled `FindingCatalogue`.

**Other as-built notes.** `IClqBaselineConfig` carries 7 column letters + `AllowedAnswers` + `DeviationThreshold`
but **no `ProvidedByColumn`** (provided-by reaches the baseline via the role-map selector, not config). The CLQ
baseline set is `ClqBaselineFindings.All` (18 descriptors keyed by string id), not referencing v01's `ClqFinding`
enum. The §3.9 equivalence guard was built (chunk A). The within-year column-change procedure these decisions
enable is documented operationally in the `auditor-change-runbook` skill and the CLAUDE.md checklist.
