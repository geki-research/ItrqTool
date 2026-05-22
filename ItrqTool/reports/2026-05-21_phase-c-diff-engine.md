# Phase C diff engine — 2026-05-21

## Adaptation summary

a. All four production files duplicated from CLQ namespace into `ItrqTool.Tasks.RiskLevelQuestionDiff`.
b. `DvDisplayFormatter`: namespace changed only (`ControlLevelQuestionDiff` → `RiskLevelQuestionDiff`).
c. `HungarianAlgorithm`: namespace changed only.
d. `TextSimilarity`: namespace changed only.
e. `QuestionDiffEngine`: parameter types changed from `AuditQuestion` to `RiskLevelQuestion`; `ExplanationChanged` flag added (`explanationSim < 1.0` on `ExplanationPrompt`); `ChangedQuestion` constructor updated to 9-field signature (no `OldExplanation`/`NewExplanation` fields); no chapter concept; `SectionName` only (no `ChapterName`); `IsDvChanged` uses `RiskLevelQuestion` fields.
f. All four test files duplicated with namespace/class-name changes from CLQ to RLQ.
g. 7 CLQ-specific tests dropped: `ExtractNumber_WithPrefix`, `ExtractNumber_NoPrefix`, `StripPrefix_RemovesNumberPrefix`, `StripPrefix_NoPrefix`, `ExtractNumber_OptionalParen`, `StripPrefix_OptionalParen`, `Diff_PrefixDoesNotAffectMatching`.
h. 4 new `ExplanationChanged` tests added to `RlqQuestionDiffEngineTests`: explanation unchanged, explanation changed, explanation and text both changed, explanation change does not affect matching decision.
i. `ChangedQuestion` exact 9-field constructor confirmed from Phase A frozen file: `(OldQuestion, NewQuestion, SimilarityScore, SecondBestSimilarity, TextChanged, ExplanationChanged, NumberChanged, DvChanged, CfChanged)`.

## Build verification

```
$ dotnet build ItrqTool.slnx --no-incremental -warnaserror

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:31.01
```

## Architecture tests

```
$ dotnet test tests/ItrqTool.Architecture.Tests

Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14, Duration: 3 s
```

## New RLQ tests (58 total: 5 + 9 + 7 + 37)

```
$ dotnet test tests/ItrqTool.Tasks.Tests --filter "FullyQualifiedName~Rlq"

Passed!  - Failed:     0, Passed:    58, Skipped:     0, Total:    58, Duration: 742 ms
```

## Full test suite

```
$ dotnet test ItrqTool.slnx

ItrqTool.Domain.Tests       Passed!  - Failed: 0, Passed:  13, Skipped: 0
ItrqTool.Application.Tests  Passed!  - Failed: 0, Passed:  12, Skipped: 0
ItrqTool.Infrastructure.Tests Passed! - Failed: 0, Passed:  55, Skipped: 0
ItrqTool.Tasks.Tests        Passed!  - Failed: 0, Passed: 170, Skipped: 0
ItrqTool.Integration.Tests  Passed!  - Failed: 0, Passed:  40, Skipped: 0
ItrqTool.Architecture.Tests Passed!  - Failed: 0, Passed:  14, Skipped: 0
```

## Cross-namespace guard

```
grep ControlLevelQuestionDiff src/ItrqTool.Tasks/RiskLevelQuestionDiff/ → No files found
grep ControlLevelQuestionDiff tests/.../RlqQuestionDiffEngineTests.cs   → No files found
```

## Forbidden-directory guard

New files are exclusively in:
- `src/ItrqTool.Tasks/RiskLevelQuestionDiff/` (4 files)
- `tests/ItrqTool.Tasks.Tests/` (4 files: Rlq*.cs)

No modifications to CLQ namespace, Domain, Infrastructure, Presentation, CLAUDE.md, workflow JSON, or any Phase A/B frozen file.

## Diff stats (untracked new files)

```
$ git status --porcelain

?? ItrqTool/src/ItrqTool.Tasks/RiskLevelQuestionDiff/DvDisplayFormatter.cs
?? ItrqTool/src/ItrqTool.Tasks/RiskLevelQuestionDiff/HungarianAlgorithm.cs
?? ItrqTool/src/ItrqTool.Tasks/RiskLevelQuestionDiff/QuestionDiffEngine.cs
?? ItrqTool/src/ItrqTool.Tasks/RiskLevelQuestionDiff/TextSimilarity.cs
?? ItrqTool/tests/ItrqTool.Tasks.Tests/RlqDvDisplayFormatterTests.cs
?? ItrqTool/tests/ItrqTool.Tasks.Tests/RlqHungarianAlgorithmTests.cs
?? ItrqTool/tests/ItrqTool.Tasks.Tests/RlqQuestionDiffEngineTests.cs
?? ItrqTool/tests/ItrqTool.Tasks.Tests/RlqTextSimilarityTests.cs
```

