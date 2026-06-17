# Implementing an auditor-mandated questionnaire change — developer guide

This guide is for a **human developer** who needs to change a questionnaire validator because the
auditor changed the workbook template — most commonly, **adding a new within-year input column**
(removing or moving one is covered more briefly at the end). It walks the real change end to end with
actual files and code, using the change that introduced column **K (answer stability)** to CLQ v02 as
the worked example.

It applies to validators built on the version-neutral core (`clq-validation-v02` and any later
core-based validator). It does **not** apply to the frozen legacy `clq-validation` (v01) — that stack
is preserved as-is and must not be edited.

There are two companion documents:
- The `auditor-change-runbook` skill (`.claude/skills/auditor-change-runbook/`) is the condensed
  version Claude Code loads when assisting; it covers the same ground tersely.
- `docs/design/validation-core-and-clq-v02.md` is the *why* — the architecture and the scope boundary.

---

## Before you start: is this change actually in scope?

The core is a **targeted optimization for the common, minor case**: a **within-year input column** —
a cell the responder fills in, judged against the empty template and the previous-year value, **without
changing how questions are identified or matched across years**. Adding column K (answer-stability) is
exactly this case, and it's the cheap path this guide covers.

A change is **out of scope** for the in-place approach below if it touches **cross-year identity or
matching** — a new identity key, renumbered questions, a different matching basis, or a restructured
section map that feeds alignment. For those, do **not** retrofit the shared core: stand up a **new
versioned stack** (e.g. `clq-validation-v03`) and freeze the current one. (See the design doc's scope
boundary, §1.) Wholesale removal or restructuring of the existing baseline columns is likewise a larger,
deliberate change — not the cheap path.

Quick test: *"Does the change alter what a question **is**, or how this year's questions line up against
last year's?"* If no → follow this guide. If yes → new versioned stack.

---

## Part A — What to change, and where (the map)

For a `clq-validation-vXX` validator (paths shown for v02), an added within-year column touches the
following. The **production change** is the first group; everything else exists to prove it.

**Production code (the actual shipping change):**

| # | File | What you do |
|---|---|---|
| 1 | `configs/clq-vXX-validation-config.json` | Add the new column letter; if the column has a fixed set of valid answers, add its allowed-set. Mind address shifts (see the N/O note below). |
| 2 | `src/ItrqTool.Tasks/ControlLevelQuestionValidationVXX/…VXXConfig.cs` | Add the column-letter property (+ the allowed-set property); add the matching rules to `Validate()`. |
| 3 | `src/ItrqTool.Tasks/ControlLevelQuestionValidationVXX/ClqVXXQuestion.cs` | Add the payload field; if the cell carries a data-validation dropdown, add its four `…Dv*` fields. |
| 4 | `src/ItrqTool.Tasks/ControlLevelQuestionValidationVXX/ClqVXXProfile.cs` | Read the column in `RecordFactory`; if it has a dropdown, add a `DvRole`; add the check via an **extension primitive** (`RequiredInputCell` and/or `FrozenConstraintCell`). |

**Trial + tests (prove the new column behaves):**

| # | File | What you do |
|---|---|---|
| 5 | `tests/ItrqTool.Integration.Tests/ClqVXX/ClqVXXQuestionSpec.cs` | Add a nullable field for the new column (the small DTO the test writer consumes). |
| 6 | `tests/ItrqTool.Integration.Tests/ClqVXX/ClqVXXWorkbookWriter.cs` | Write the new cell; apply its dropdown to **every** row of **every** workbook (incl. the template). |
| 7 | `tests/ItrqTool.Integration.Tests/ClqVXX/ClqVXXBaselineFactory.cs` | Give the new cell a valid baseline value on current + previous, and `null` on the template — so the baseline stays at zero findings. |
| 8 | `tests/ItrqTool.Tasks.Tests/ControlLevelQuestionValidationVXX/ClqVXXProductionConfigAssetTests.cs` | Assert the new column letter (+ allowed-set) in the production config. |
| 9 | `tests/ItrqTool.Integration.Tests/ClqVXX/ClqVXXStabilityPerturbationTests.cs` (or a sibling) | Add an exact-set test proving the new findings fire — and that nothing spurious does. |

**Docs:**

| # | File | What you do |
|---|---|---|
| 10 | the `clq-validation-vXX` skill + `CLAUDE.md` | Update the skill's column map/findings; bump the test baseline **only if** the test count changed. |

> **The N/O address-shift trap.** Inserting a column *between existing mapped columns* pushes the later
> ones along. In v02, putting answer-stability at **K** shifted provided-by **M → N** and xref-id
> **N → O**. That shift shows up in **four** places — the config (1), the profile's `RecordFactory` (4),
> the workbook writer (6), and the asset test (8). Miss one and reads land in the wrong column. If you
> instead **append** the new column *after* the last mapped one, there's no cascade. Always check.

---

## Part B — Worked example: adding column K (answer stability) to CLQ v02

The auditor added column **K**, a Yes/No dropdown ("will this answer change within 6 months?"), and in
doing so shifted provided-by to **N** and xref-id to **O**. Three findings were wanted: K blank, K not
in {Yes, No}, and K's dropdown rule changed vs the template. Here is the actual change, in order.

### 1 — Config asset: `configs/clq-v02-validation-config.json`
Add the column letter and its allowed-set, and fix the shifted columns:
```json
"ProvidedByColumn": "N",
"XrefIdColumn": "O",
"AnswerStabilityColumn": "K",
...
"AllowedStabilityAnswers": ["Yes", "No"],
```
`AllowedStabilityAnswers` is only needed because K has a *fixed* valid set. A free-text column wouldn't
have one. (Note: there is **no code default** for the allowed-set — an empty list is invalid and fails
loud at load, so the production asset must supply it.)

### 2 — Config record: `…/ControlLevelQuestionValidationV02Config.cs`
Add the two `init` properties and the two `Validate()` rules:
```csharp
public string AnswerStabilityColumn { get; init; } = "";
public IReadOnlyList<string> AllowedStabilityAnswers { get; init; } = [];
```
```csharp
// inside Validate():
ValidateColumnLetter(nameof(AnswerStabilityColumn), AnswerStabilityColumn, errors);
...
if (AllowedStabilityAnswers.Count == 0)
    errors.Add("AllowedStabilityAnswers must not be empty.");
```
The config loads through the **generic** `ConfigLoader.Load<…>(json, c => c.Validate())` — do **not**
add a per-version loader class.

### 3 — Question record: `…/ClqV02Question.cs`
Add the payload field and (because K is a dropdown) its four DV fields:
```csharp
// ── v02 additions ──
string? AnswerStability,
string? AnswerStabilityDvType,
string? AnswerStabilityDvFormula,
string? AnswerStabilityDvOperator,
string? AnswerStabilityDvFormula2
```
The four `…Dv*` fields follow the same pattern as the existing `AnswerDv*` group; they start `null` and
are filled by the `DvPatcher` (wired in the next step).

### 4 — Profile: `…/ClqV02Profile.cs` (the heart)
Three edits inside `ClqV02Profile.Build`.

**(a) Read the column in `RecordFactory`** (and note the shifted N/O reads):
```csharp
ProvidedBy:        GetCellText(ctx.Row, config.ProvidedByColumn),   // now N
...
AnswerStability:           GetCellText(ctx.Row, config.AnswerStabilityColumn),  // K
AnswerStabilityDvType:      null,   // DvPatcher fills these
AnswerStabilityDvFormula:   null,
AnswerStabilityDvOperator:  null,
AnswerStabilityDvFormula2:  null,
```

**(b) Add a DV-role** so the patcher reads K's dropdown into those four fields:
```csharp
(Column: config.AnswerStabilityColumn,
 ApplyDv: (ClqV02Question q, ExcelCellStructure cell) => q with
 {
     AnswerStabilityDvType     = cell.DataValidationType,
     AnswerStabilityDvFormula  = cell.DataValidationFormula,
     AnswerStabilityDvOperator = cell.DataValidationOperator,
     AnswerStabilityDvFormula2 = cell.DataValidationFormula2,
 }),
```

**(c) Add the checks as extension primitives** — do not hand-write a bespoke check when a primitive
fits. `RequiredInputCell` gives you the *missing* and *not-in-allowed-set* findings; `FrozenConstraintCell`
gives you the *dropdown-rule-changed* finding:
```csharp
new RequiredInputCell<ClqV02Question>(
    valueSelector:      q => q.AnswerStability,
    providedBySelector: q => q.ProvidedBy,
    role:               "answer-stability",
    column:             config.AnswerStabilityColumn,
    allowed:            config.AllowedStabilityAnswers),   // from config, never a literal
new FrozenConstraintCell<ClqV02Question>(
    dvTypeSelector:     q => q.AnswerStabilityDvType,
    dvOperatorSelector: q => q.AnswerStabilityDvOperator,
    dvFormulaSelector:  q => q.AnswerStabilityDvFormula,
    dvFormula2Selector: q => q.AnswerStabilityDvFormula2,
    providedBySelector: q => q.ProvidedBy,
    role:               "answer-stability",
    column:             config.AnswerStabilityColumn),
```
That's the whole production change. The `role` string (`"answer-stability"`) becomes the finding-id
stem: `input-cell.answer-stability.missing`, `…not-in-allowed-set`, `constraint.answer-stability.validation-rule-changed`.

> *Aside, so it doesn't trip you up:* `LayoutParser.Parse(config.ChapterRows, config.SectionRows,
> config.TextColumn, config.TextColumn, config.TextColumn)` passes `TextColumn` three times on purpose —
> chapter, section, and question header text all live in column D in this template. You don't touch this
> when adding a column.

### 5 — Test DTO: `…/ClqV02QuestionSpec.cs`
The trial writer consumes a small per-row spec. Add a nullable field for the new column:
```csharp
string? AnswerStability = null
```

### 6 — Writer: `…/ClqV02WorkbookWriter.cs`
Add an optional per-row DV-override parameter (used later by the perturbation test), write the cell, and
— importantly — apply the dropdown to **every** row of **every** workbook:
```csharp
public static void Write(..., IReadOnlyDictionary<int,string>? stabilityDvOverrides = null)
...
if (q.AnswerStability != null) ws.Cell(q.RowNumber, "K").Value = q.AnswerStability;
if (q.ProvidedBy      != null) ws.Cell(q.RowNumber, "N").Value = q.ProvidedBy;  // shifted
ws.Cell(q.RowNumber, "O").Value = q.XrefId;                                      // shifted
...
const string DefaultStabilityDv = "\"Yes,No\"";
foreach (var q in descriptor.Questions)
    ws.Cell(q.RowNumber, "K").CreateDataValidation().List(
        stabilityDvOverrides?.GetValueOrDefault(q.RowNumber) ?? DefaultStabilityDv);
```
> **Invariant — the template must carry the dropdown even though its value is blank.** The DV loop runs
> over every row of every workbook, including the template (whose K value is `null`). `FrozenConstraintCell`
> compares the *current* dropdown against the *template's* dropdown; if the template carried no dropdown,
> there'd be nothing to compare and the rule-changed finding could never fire. This mirrors how the blank
> template H cell still carries the answer dropdown.

### 7 — Baseline factory: `…/ClqV02BaselineFactory.cs`
The baseline trio must produce **zero findings**. For K that means: a valid value on current and
previous, and `null` on the template:
```csharp
// current + previous:
AnswerStability: "Yes"      // ∈ AllowedStabilityAnswers → no "missing"/"not-in-set"
// template:
AnswerStability: null       // blank cell, but the writer still gives it the "Yes,No" DV
```
With both workbooks carrying the same `"Yes,No"` dropdown, `FrozenConstraintCell` sees no change → no
rule-changed finding. **The baseline-zero contract for any new column: a valid value AND both workbooks
carry the same DV.** (This factory also parses `config.ChapterRows` from strings to ints before ordering,
and derives sections via `LayoutParser` since the v02 config has no `ParsedSections` — neither is K-specific,
but you'll see them.)

### 8 — Asset test: `…/ClqV02ProductionConfigAssetTests.cs`
Pin the new column letter and allowed-set in the existing config-load fact:
```csharp
config.ProvidedByColumn.Should().Be("N");
config.XrefIdColumn.Should().Be("O");
config.AnswerStabilityColumn.Should().Be("K");
config.AllowedStabilityAnswers.Should().Equal("Yes", "No");
```
(Adding assertions to an existing `[Fact]` doesn't change the test count — so no baseline bump for this.)

### 9 — Perturbation test: `…/ClqV02StabilityPerturbationTests.cs`
Prove the three findings as an **exact set** — seed one anomaly per row, then assert count + each
finding + no extras:
```csharp
7 => q with { AnswerStability = null },     // → MissingResponse/Error @ K7
8 => q with { AnswerStability = "Maybe" },  // → MissingResponse/Fatal @ K8
// row 9: narrow the K dropdown vs the template
var stabilityDvOverrides = new Dictionary<int,string> { [9] = "\"Yes\"" }; // → FrozenConstraint/Error @ K9
```
```csharp
report.Findings.Should().HaveCount(3, ...);                 // 1) nothing missing
foreach (var ex in expected)
    report.Findings.Should().Contain(f =>                   // 2) each expected finding,
        f.Check == ex.Check && f.Evaluation == ex.Evaluation &&
        f.CellAddresses == ex.CellAddresses &&
        f.CheckResult.Contains(ex.CheckResultSubstring));   //    matched on 4 fields
unexpected.Should().BeEmpty(...);                           // 3) nothing spurious
```
Assert on the four observable fields — `Check`, `Evaluation`, `CellAddresses` (e.g. `"K7"`), and a
substring of `CheckResult`. The internal string finding-ids are **not** fields on the finding.

### 10 — Build, test, and land
Run the affected projects, then the full suite:
```
dotnet test tests/ItrqTool.Tasks.Tests          # asset test (fast)
dotnet test tests/ItrqTool.Integration.Tests    # trial/perturbation (slower; the v02 integration suite is a few minutes)
dotnet test                                      # full solution — must be all green, 0 skipped
```
Green means: the baseline trio still yields zero findings, and your perturbation test shows exactly the
new findings. If you **added** test methods (so the count rose), bump the test-count baseline in `CLAUDE.md`
(the `Integration … — N total` line) **as its own `docs:` commit** — the repo keeps the baseline bump
separate from the feature change. If you only folded assertions into existing facts, the count is unchanged
and there's nothing to bump. Update the `clq-validation-v02` skill's column map/findings to match.

---

## Removing a within-year column  *(not yet exercised — reason it through)*

It's the reverse of adding: from the config drop the column letter, the allowed-set, and the matching
`Validate()` rules; from the question record drop the payload + DV fields; from the profile drop the
`RecordFactory` read, the `DvRole`, and the extension primitive(s); delete the now-dead findings and the
perturbation test that drove them; remove the writer cell + DV block, the spec field, and the baseline
default; update the asset test. **Watch the same address cascade in reverse** — removing a middle column
pulls later columns back. If anything downstream references columns by absolute letter (the feedback
checklist assembler, other checks), reconcile those too.

## Moving or renaming a column  *(not yet exercised — reason it through)*

- **Just the letter moved, same meaning:** change the letter in the config, the asset test, and the
  writer. The record, profile, and findings read the column *through the config*, so they're letter-
  agnostic — usually no change there. Watch for a move that **collides** with another role's column (that's
  the N→O cascade again).
- **The meaning or allowed-set changed:** treat it as a removal of the old rule plus an addition of the
  new one (new allowed-set, possibly new findings).

---

## See also

- `.claude/skills/auditor-change-runbook/` — the condensed runbook Claude Code loads when assisting.
- `CLAUDE.md` → "Implementing an auditor-mandated column change" (the always-on checklist) and
  "Validation tasks" (the core pattern and the within-year/cross-year boundary).
- `.claude/skills/clq-validation-v02/` — the reference for the v02 stack this example lives in.
- `docs/design/validation-core-and-clq-v02.md` — the architecture and the *why* behind the scope boundary.
