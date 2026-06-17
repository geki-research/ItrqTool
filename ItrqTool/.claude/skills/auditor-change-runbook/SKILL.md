---
name: auditor-change-runbook
description: Step-by-step runbook for implementing an auditor-mandated questionnaire change on a core-based validator (clq-validation-v02 and later) — adding, removing, or moving a within-year input column, expressed through the QuestionnaireValidation core's declarative extension primitives (RequiredInputCell, FrozenConstraintCell) rather than bespoke checks. Leads with the SCOPE GATE: a within-year input-cell change is the cheap supported path (worked end-to-end below); a change to cross-year question identity or the matching basis is out of scope and must duplicate-and-defer to a new versioned stack. Covers the full surface a column change touches: config JSON plus config record/Validate, the per-version question record and its DV fields, the profile (RecordFactory, DvRoles, extension primitives), the trial workbook writer and baseline factory, the asset/baseline/perturbation/end-to-end tests, and the docs/baseline bump. The add-column path is DEMONSTRATED (v02 "add column K"); remove and move are INFERRED. Load when the auditor changes a questionnaire template, when adding/removing/altering a validation input column, or when deciding whether a questionnaire change fits the core or needs a new version.
---

# Auditor-change runbook — implementing a questionnaire template change

Auditors periodically revise a questionnaire template (a new input column, a dropped one, a moved one,
a changed allowed-set). This runbook is the step-by-step for absorbing such a change in a validator
**built on the version-neutral core** (`ItrqTool.Tasks.QuestionnaireValidation` — `clq-validation-v02`
and later). It does NOT apply to `clq-validation` (v01), which is frozen-legacy bespoke and must not be
extended.

The condensed always-on version is the CLAUDE.md checklist "Implementing an auditor-mandated column
change"; the worked instance is the `clq-validation-v02` skill. This runbook is the long form.

## §1 — SCOPE GATE (decide this first)

Classify the change before touching code:

- **Within-year input-cell change** — a new/removed/moved cell whose correctness is judged against the
  empty template and the previous-year value *within the current structure*, WITHOUT changing how
  questions are identified or matched across years. → **Cheap, supported path.** Follow §2/§3/§4.
- **Cross-year-relevant change** — anything that changes question **identity**, the **matching basis**,
  the cross-year alignment, or the corpus shape such that previous-year responses no longer align the
  same way (a new identity key, renumbered questions, a restructured section map feeding alignment). →
  **OUT OF SCOPE for an in-place core extension. Duplicate-and-defer:** stand up a new versioned stack
  (e.g. `clq-validation-v03`) and freeze the current one.

Why the gate: the core's alignment engine is version-neutral, but each per-version **profile** encodes
that version's columns and gating. A within-year cell rides on the existing alignment untouched. A
cross-year identity/matching change ripples through alignment AND the baseline gating, and risks the
frozen contract of the shipped version. Branch the version; do not retrofit the shared core.

## §2 — ADD a within-year input column  *(DEMONSTRATED — v02 "add column K")*

1. **Config asset** (`configs/clq-vXX-validation-config.json`): add the column letter; if the column has
   a fixed allowed-set, add it (e.g. `AllowedStabilityAnswers`). Mind address shifts — inserting a column
   may push later roles (in v02, answer-stability at **K** pushed provided-by **M→N**, xref-id **N→O**).
2. **Config record** (`…VXXConfig : IClqBaselineConfig`): add the column property (+ the allowed-set
   property); add `Validate()` rules (non-blank letter; non-empty allowed-set). Loading stays on the
   generic `ConfigLoader.Load<…>(json, c => c.Validate())` — do NOT add a per-version loader.
3. **Question record** (`…VXXQuestion : IAlignmentIdentity`): add the payload field; if the cell carries
   data-validation, add its four DV fields (`…DvType` / `…DvFormula` / `…DvOperator` / `…DvFormula2`).
4. **Profile** (`…Profile.Build`): read the new column in `RecordFactory`; if it has DV, declare a
   `DvRole` (role name → the column) so `DvPatcher` fills its DV fields; then express the check via the
   **extension primitives** — `RequiredInputCell` (emits *missing* when blank, *not-in-allowed-set* when
   present-but-invalid) and/or `FrozenConstraintCell` (emits *validation-rule-changed* when the current DV
   differs from the template), both keyed on the new role with `allowed` from config. Do NOT hand-write a
   bespoke check when a primitive fits.
5. **Trial infra**: extend the trial workbook writer to emit the new column (and apply its DV to every
   row); add an optional per-row override param for perturbations (current-only). The baseline factory sets
   a consistent default for the new cell so the baseline stays **zero findings**.
6. **Tests**: production-config asset test (assert the new column letter + the section-parse count);
   baseline zero-findings; a perturbation test proving the new findings as an **exact set** (no extras);
   the end-to-end workflow test through to the checklist.
7. **Docs**: update the `clq-validation-vXX` skill (column map, findings, profile); bump the CLAUDE.md test
   baseline **iff** the count changed; if this is a brand-new version, add its skill to the CLAUDE.md
   "Current skills:" list and the skills overview.

**Worked example (v02):** column **K** = answer-stability, `AllowedStabilityAnswers ["Yes","No"]`, role
`answer-stability`, primitives `RequiredInputCell` + `FrozenConstraintCell` → three findings
(`input-cell.answer-stability.missing` Error, `…not-in-allowed-set` Fatal,
`constraint.answer-stability.validation-rule-changed` Error), all at `K{row}`. See the `clq-validation-v02`
skill and the `ClqV02*` trial classes (`ClqV02Profile`, `ClqV02WorkbookWriter`,
`ClqV02StabilityPerturbationTests`, `ClqV02EndToEndWorkflowTests`).

## §3 — REMOVE a within-year input column  *(INFERRED — not yet exercised)*

Reverse of §2: drop the column property + allowed-set + `Validate()` rules from the config; drop the
payload + DV fields from the question record; remove the `RecordFactory` read, the `DvRole`, and the
extension-primitive registration from the profile; delete the associated findings. Update the asset test
(the removed column is no longer pinned), the perturbation tests (drop the removed findings), the writer,
the skill, and the baseline (bump iff count changed). **Caution:** if downstream (e.g. the feedback-checklist
assembler) or other checks reference columns by absolute letter, a removal that shifts addresses must be
reconciled there too.

## §4 — MOVE / ALTER a within-year input column  *(INFERRED — not yet exercised)*

- **Letter move only** (same semantics): change the column letter in the config + the asset test + the
  writer. The record/profile/findings read via config, so they are letter-agnostic — usually no record or
  check change. **Caution:** a move that collides with another role's column needs the shift handled
  explicitly (cf. v02's K-insert cascading M→N→O).
- **Semantics / allowed-set change**: treat as a §3 remove of the old rule plus a §2 add of the new one
  (new allowed-set, possibly new findings).

## §5 — Guardrails

- Express checks through the primitives; reach for a bespoke check only when no primitive fits (and then
  document why).
- Never add a per-version config loader — the generic `ConfigLoader` + the record's `Validate()` is the
  contract.
- Never modify `clq-validation` (v01); it is frozen-legacy.
- Never retrofit the shared core for a cross-year change (see §1) — branch the version.
- Always prove findings as an exact set; keep the baseline factory at zero findings.

## §6 — See also

- `docs/guides/auditor-mandated-changes.md` — the **human** developer walkthrough (real paths, before/after
  snippets, build/test/land); use it when a person is implementing the change by hand.
- CLAUDE.md → "Implementing an auditor-mandated column change" (the condensed checklist) and "Validation
  tasks" (the core pattern + the within-year/cross-year boundary).
- `clq-validation-v02` skill — the worked instance this runbook generalizes.
- `docs/design/validation-core-and-clq-v02.md` — the as-built rationale (core design, the scope boundary).
