using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Tasks.Configuration;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.GeneralDataValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;
using ItrqTool.Tasks.Shared;
using ItrqTool.Tasks.WorksheetStructure;

namespace ItrqTool.Tasks.GeneralDataInject;

/// <summary>
/// GD inject (v01 → v02). Reads the previous-year v01 response and the current-year v02
/// template, aligns them with <see cref="GdInjectAligner"/> (a pure qid-join cross-version
/// aligner), maps the confident matches to reference cell writes via <see cref="GdInjectMapper"/>,
/// and writes the populated workbook to the working-dir <c>output</c> path. Final placement is the
/// downstream StaticFileSink's job — this task performs NO File.* / SaveAs.
/// </summary>
/// <remarks>
/// Mirrors <c>RiskLevelQuestionInjectV01ToV02Task</c>, differing only in the per-ANSWER-anchor-row
/// grain (GD questions span multiple answers) and that GD builds its parse layout inline from the
/// config (no v02 profile exists — v02 validation is deferred).
/// <para>
/// Parameters: <c>configurationFullFilename</c> (absolute path, or relative to the application
/// directory (AppContext.BaseDirectory); the two referenced validation configs resolve relative to ITS directory).
/// Inputs: <c>previousResponse</c> (v01), <c>currentTemplate</c> (v02).
/// Output: <c>output</c>.
/// Succeeded semantics: false only on missing inputs/params, missing/invalid config files, the
/// structure gate (asset/mismatch), or parse errors. Type-compatibility policy messages
/// (Warning/Error) do NOT fail the task — a partial-deliverable inject still ships.
/// </para>
/// </remarks>
public sealed class GeneralDataInjectV01ToV02Task : IWorkflowTask
{
    private readonly IExcelStructureReader _reader;
    private readonly IExcelTemplateWriter _writer;
    private readonly IWorksheetStructureMediator _mediator;

    public GeneralDataInjectV01ToV02Task(
        IExcelStructureReader reader,
        IExcelTemplateWriter writer,
        IWorksheetStructureMediator mediator)
    {
        _reader = reader;
        _writer = writer;
        _mediator = mediator;
    }

    public string TaskType => "GeneralDataInject_v01_to_v02";

    public async Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        try
        {
            if (!TryGetParam(ctx, "configurationFullFilename", out var injectConfigPathRaw))
            {
                messages.Add(new(MessageSeverity.Error,
                    "Required parameter missing or empty: configurationFullFilename.",
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }
            var injectConfigPath = ConfigPathResolver.Resolve(injectConfigPathRaw);

            if (!TryGetInput(ctx, "previousResponse", out var previousPath, messages) ||
                !TryGetInput(ctx, "currentTemplate", out var currentPath, messages))
            {
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            var outputPath = ctx.OutputPaths["output"];

            if (!File.Exists(injectConfigPath))
            {
                messages.Add(new(MessageSeverity.Error,
                    $"File not found: configurationFullFilename: {injectConfigPath}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            // ── Load the inject config + the two referenced validation configs ──
            GdInjectConfig injectConfig;
            GdV02Config currentConfig;
            GdV01Config previousConfig;
            try
            {
                var injectJson = await File.ReadAllTextAsync(injectConfigPath, ct);
                injectConfig = ConfigLoader.Load<GdInjectConfig>(injectJson, c => c.Validate());

                var configDir = Path.GetDirectoryName(injectConfigPath) ?? "";
                var currentConfigPath  = Path.Combine(configDir, injectConfig.CurrentConfigFilename);
                var previousConfigPath = Path.Combine(configDir, injectConfig.PreviousConfigFilename);

                var configMissing = new List<string>();
                if (!File.Exists(currentConfigPath))  configMissing.Add($"CurrentConfigFilename: {currentConfigPath}");
                if (!File.Exists(previousConfigPath)) configMissing.Add($"PreviousConfigFilename: {previousConfigPath}");
                if (configMissing.Count > 0)
                {
                    messages.Add(new(MessageSeverity.Error,
                        "Referenced config file(s) not found: " + string.Join("; ", configMissing),
                        DateTimeOffset.Now));
                    return new TaskResult(Succeeded: false, messages, sw.Elapsed);
                }

                var currentJson  = await File.ReadAllTextAsync(currentConfigPath,  ct);
                currentConfig = ConfigLoader.Load<GdV02Config>(currentJson, c => c.Validate());

                var previousJson = await File.ReadAllTextAsync(previousConfigPath, ct);
                previousConfig = ConfigLoader.Load<GdV01Config>(previousJson, c => c.Validate());
            }
            catch (ConfigException ex)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Configuration error ({injectConfigPath}): {ex.Message}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            var missingInputs = new List<string>();
            if (!File.Exists(currentPath))  missingInputs.Add($"currentTemplate: {currentPath}");
            if (!File.Exists(previousPath)) missingInputs.Add($"previousResponse: {previousPath}");
            if (missingInputs.Count > 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    "Input file(s) not found: " + string.Join("; ", missingInputs), DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            // ── Structure gate (before parse/read) — SOURCE gd-v01, TARGET gd-v02 ──
            var gate = StructureGate.VerifyAll(_mediator, new[]
            {
                (previousPath, new WorksheetSchemaRef("gd", "v01")),   // SOURCE
                (currentPath,  new WorksheetSchemaRef("gd", "v02")),   // TARGET
            });
            if (gate.AssetFailed)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Worksheet-structure schema asset error: {gate.AssetErrorReason}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }
            if (gate.StructureFindings.Count > 0)
            {
                foreach (var f in gate.StructureFindings)
                    messages.Add(new(MessageSeverity.Error, f.CheckResult, DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            // ── Parse both workbooks (layout built inline from config — GD has no v02 profile) ──
            var currentLayout  = BuildLayout(currentConfig.TextColumn,  currentConfig.Sections);
            var previousLayout = BuildLayout(previousConfig.TextColumn, previousConfig.Sections);

            var parseMessages = new List<TaskMessage>();

            var current = GdV02QuestionParser.Parse(
                _reader.ReadRows(currentPath, currentConfig.SheetName), currentLayout, currentConfig, parseMessages);

            ct.ThrowIfCancellationRequested();

            var previous = GdV01QuestionParser.Parse(
                _reader.ReadRows(previousPath, previousConfig.SheetName), previousLayout, previousConfig, parseMessages);

            messages.AddRange(parseMessages);
            if (parseMessages.Any(m => m.Severity == MessageSeverity.Error))
            {
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            // ── Dual-DV-read: source H (v01 answer anchor rows) and target H (v02 answer anchor rows) ──
            var sourceHByAnchorRow = BuildSourceHLookup(previousPath, previousConfig.SheetName, previousConfig, previous.Questions);
            var targetHByAnchorRow = BuildTargetHLookup(currentPath,  currentConfig.SheetName,  currentConfig,  current.Questions);

            // ── Align → Map → Write ──
            var alignment = GdInjectAligner.Align(current.Questions, previous.Questions);

            var (cells, mapMessages) = GdInjectMapper.Map(
                alignment, currentConfig, sourceHByAnchorRow, targetHByAnchorRow);

            ct.ThrowIfCancellationRequested();

            _writer.Populate(currentPath, currentConfig.SheetName, cells, outputPath);

            messages.AddRange(mapMessages);

            int agreeCount     = alignment.Matches.Count(m => m.Outcome == CrossYearOutcome.Agree);
            int ambiguousCount = alignment.Matches.Count(m =>
                m.Outcome is CrossYearOutcome.XrefIdConflict
                          or CrossYearOutcome.NewXrefIdWithLookalike
                          or CrossYearOutcome.SameXrefIdTextDiverged);
            int unmatchedCount = alignment.Matches.Count(m =>
                m.Outcome is CrossYearOutcome.Neither
                          or CrossYearOutcome.NotEvaluatedMalformedKey);

            messages.Add(new(MessageSeverity.Info,
                $"Inject complete: {agreeCount} confident match(es) injected, " +
                $"{ambiguousCount} ambiguous (left untouched), " +
                $"{unmatchedCount} unmatched. Wrote {cells.Count} cell(s) to {Path.GetFileName(outputPath)}.",
                DateTimeOffset.Now));

            return new TaskResult(Succeeded: true, messages, sw.Elapsed);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            ctx.Logger.LogError(ex, "GD inject (v01→v02) failed: {Message}", ex.Message);
            messages.Add(new(MessageSeverity.Error, ex.Message, DateTimeOffset.Now));
            return new TaskResult(Succeeded: false, messages, sw.Elapsed);
        }
    }

    // GD has no v02 ValidationPipelineProfile (v02 validation is deferred), so the parse layout is
    // built inline directly from config.Sections — identical to GdV01Profile.Build's Layout (sections
    // only, every name read from the text column).
    private static QuestionnaireLayout BuildLayout(string textColumn, IReadOnlyList<GdSectionSpec> sections) =>
        new(QuestionTextColumn: textColumn,
            Chapters: [],
            Sections: sections
                .Select(s => new LayoutSection(s.HeaderRow, s.FirstDataRow, s.LastDataRow, textColumn))
                .ToList());

    // Source H dual-read: v01 answer AnchorRow → (DV-type, native value, TextValue). Keyed by
    // answer anchor row (not question row) — the per-answer-grain analog of RlqInjectMapper's
    // BuildSourceHLookup. BL-053 P4b-G2: TextValue added — the DV-governed literal InjectionValueGuard
    // evaluates against the target's DV rule; never a re-stringified NativeValue.
    private IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)> BuildSourceHLookup(
        string path, string sheetName, GdV01Config config,
        IReadOnlyList<GdV01Question> questions)
    {
        var anchorRows = questions.SelectMany(q => q.Answers).Select(a => a.AnchorRow).Distinct().ToList();
        if (anchorRows.Count == 0)
            return new Dictionary<int, (string?, object?, string?)>();

        var col    = config.AnswerColumn;
        var minRow = anchorRows.Min();
        var maxRow = anchorRows.Max();
        var cells  = _reader.ReadCells(path, sheetName, [$"{col}{minRow}:{col}{maxRow}"]);

        var result = new Dictionary<int, (string?, object?, string?)>(anchorRows.Count);
        foreach (var row in anchorRows)
        {
            cells.TryGetValue($"{col}{row}", out var cell);
            result[row] = (cell?.DataValidationType, cell?.NativeValue, cell?.TextValue);
        }
        return result;
    }

    // BL-053 P4b-G1 (additive, read-phase only): reads the target answer cell's FULL data
    // validation rule (type, operator, both formulas, resolved List vocabulary), mirroring the
    // RLQ inject-R1 target-DV read idiom (inline-List parsed here; range-ref / named-range
    // resolved via the shared DvRangeRefResolver, reused as-is against this task's own _reader
    // and currentPath), adapted to GD's per-ANSWER AnchorRow grain. The mapper still derives its
    // decision from Type only (byte-equivalent to today) — the richer fields are populated but
    // unread until a later phase wires the injection value guard in.
    private IReadOnlyDictionary<int, GdTargetDvHolder> BuildTargetHLookup(
        string path, string sheetName, GdV02Config config,
        IReadOnlyList<GdV02Question> questions)
    {
        var anchorRows = questions.SelectMany(q => q.Answers).Select(a => a.AnchorRow).Distinct().ToList();
        if (anchorRows.Count == 0)
            return new Dictionary<int, GdTargetDvHolder>();

        var col    = config.AnswerColumn;
        var minRow = anchorRows.Min();
        var maxRow = anchorRows.Max();
        var cells  = _reader.ReadCells(path, sheetName, [$"{col}{minRow}:{col}{maxRow}"]);

        IReadOnlyList<GdTargetDvHolder> holders = anchorRows
            .Select(row =>
            {
                cells.TryGetValue($"{col}{row}", out var cell);
                return new GdTargetDvHolder(
                    row,
                    cell?.DataValidationType,
                    cell?.DataValidationOperator,
                    cell?.DataValidationFormula,
                    cell?.DataValidationFormula2,
                    cell is null ? null : InlineListValues(cell));
            })
            .ToList();

        holders = DvRangeRefResolver.Resolve(
            _reader, path, sheetName, holders,
            dvTypeSelector:            h => h.Type,
            dvFormulaSelector:         h => h.Formula,
            currentListValuesSelector: h => h.ListValues,
            stampListValues:           (h, vals) => h with { ListValues = vals });

        return holders.ToDictionary(h => h.AnchorRow);
    }

    // Mirrors the inline-List idiom frozen in RlqV01Profile / GdDvPatcher / CellRangeInjectTask:
    // a List-typed cell whose source classifies as Inline → its parsed members; otherwise null
    // (range-ref / named-range resolved next, by DvRangeRefResolver.Resolve above).
    private static IReadOnlyList<string>? InlineListValues(ExcelCellStructure cell)
        => string.Equals(cell.DataValidationType, "List", StringComparison.OrdinalIgnoreCase)
           && DvListParser.ClassifySource(cell.DataValidationFormula ?? "") == DvListSourceKind.Inline
            ? DvListParser.ParseInline(cell.DataValidationFormula ?? "")
            : null;

    private static bool TryGetParam(TaskExecutionContext ctx, string key, out string value)
    {
        if (ctx.Parameters.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
        {
            value = v;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool TryGetInput(
        TaskExecutionContext ctx, string key, out string value, List<TaskMessage> messages)
    {
        if (ctx.InputPaths.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
        {
            value = v;
            return true;
        }
        messages.Add(new(MessageSeverity.Error,
            $"Required input missing or empty: {key}.", DateTimeOffset.Now));
        value = string.Empty;
        return false;
    }
}
