using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Tasks.Configuration;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;
using ItrqTool.Tasks.Shared;
using ItrqTool.Tasks.WorksheetStructure;

namespace ItrqTool.Tasks.RiskLevelQuestionInject;

/// <summary>
/// RLQ inject (v01 → v02). Reads the previous-year v01 response and the current-year v02
/// template, aligns them with <see cref="CrossFormatAligner"/>, maps the confident matches
/// to reference cell writes via <see cref="RlqInjectMapper"/>, and writes the populated
/// workbook to the working-dir <c>output</c> path. Final placement is the downstream
/// StaticFileSink's job — this task performs NO File.* / SaveAs.
/// </summary>
/// <remarks>
/// Parameters: <c>configurationFullFilename</c> (absolute path, or relative to the application
/// directory (AppContext.BaseDirectory); the two referenced validation configs resolve relative to ITS directory).
/// Inputs: <c>previousResponse</c> (v01), <c>currentTemplate</c> (v02).
/// Output: <c>output</c>.
/// Succeeded semantics: false only on missing inputs/params, missing/invalid config files,
/// or parse errors. Type-compatibility policy messages (Warning/Error) do not fail the task.
/// </remarks>
public sealed class RiskLevelQuestionInjectV01ToV02Task : IWorkflowTask
{
    private readonly IExcelStructureReader _reader;
    private readonly IExcelTemplateWriter _writer;
    private readonly IWorksheetStructureMediator _mediator;

    public RiskLevelQuestionInjectV01ToV02Task(
        IExcelStructureReader reader,
        IExcelTemplateWriter writer,
        IWorksheetStructureMediator mediator)
    {
        _reader = reader;
        _writer = writer;
        _mediator = mediator;
    }

    public string TaskType => "RiskLevelQuestionInject_v01_to_v02";

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
            RlqInjectConfig injectConfig;
            RlqV02Config currentConfig;
            RlqV01Config previousConfig;
            try
            {
                var injectJson = await File.ReadAllTextAsync(injectConfigPath, ct);
                injectConfig = ConfigLoader.Load<RlqInjectConfig>(injectJson, c => c.Validate());

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
                currentConfig = ConfigLoader.Load<RlqV02Config>(currentJson, c => c.Validate());

                var previousJson = await File.ReadAllTextAsync(previousConfigPath, ct);
                previousConfig = ConfigLoader.Load<RlqV01Config>(previousJson, c => c.Validate());
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

            // ── Parse both workbooks via their profiles ──
            var currentProfile  = RlqV02Profile.Build(currentConfig);
            var previousProfile = RlqV01Profile.Build(previousConfig);

            var gate = StructureGate.VerifyAll(_mediator, new[]
            {
                (previousPath, new WorksheetSchemaRef("rlq", "v01")),   // SOURCE
                (currentPath,  new WorksheetSchemaRef("rlq", "v02")),   // TARGET
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

            var parseMessages = new List<TaskMessage>();

            var currentRows     = _reader.ReadRows(currentPath,  currentProfile.SheetName);
            var currentQuestions = RlqV02QuestionParser.Parse(
                currentRows, currentProfile.Layout, currentConfig, parseMessages);

            ct.ThrowIfCancellationRequested();

            var previousRows      = _reader.ReadRows(previousPath, previousProfile.SheetName);
            var previousQuestions = RlqV01QuestionParser.Parse(
                previousRows, previousProfile.Layout, previousConfig, parseMessages);

            messages.AddRange(parseMessages);
            if (parseMessages.Any(m => m.Severity == MessageSeverity.Error))
            {
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            // ── ReadCells: source H (v01 anchor rows) and target H (v02 anchor rows) ──
            var sourceHByRow = BuildSourceHLookup(previousPath, previousProfile.SheetName, previousConfig, previousQuestions);
            var targetHByRow = BuildTargetHLookup(currentPath,  currentProfile.SheetName,  currentConfig,  currentQuestions);

            // ── Align → Map → Write ──
            var alignment = CrossFormatAligner.Align<RlqV02Question, RlqV01Question>(
                currentQuestions, previousQuestions);

            var (cells, mapMessages) = RlqInjectMapper.Map(alignment, currentConfig, sourceHByRow, targetHByRow);

            ct.ThrowIfCancellationRequested();

            _writer.Populate(currentPath, currentProfile.SheetName, cells, outputPath);

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
            ctx.Logger.LogError(ex, "RLQ inject (v01→v02) failed: {Message}", ex.Message);
            messages.Add(new(MessageSeverity.Error, ex.Message, DateTimeOffset.Now));
            return new TaskResult(Succeeded: false, messages, sw.Elapsed);
        }
    }

    private IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)> BuildSourceHLookup(
        string path, string sheetName, RlqV01Config config,
        IReadOnlyList<RlqV01Question> questions)
    {
        if (questions.Count == 0)
            return new Dictionary<int, (string?, object?, string?)>();

        var col    = config.AnswerColumn;
        var minRow = questions.Min(q => q.RowNumber);
        var maxRow = questions.Max(q => q.RowNumber);
        var cells  = _reader.ReadCells(path, sheetName, [$"{col}{minRow}:{col}{maxRow}"]);

        return questions.ToDictionary(
            q => q.RowNumber,
            q =>
            {
                cells.TryGetValue($"{col}{q.RowNumber}", out var cell);
                return (cell?.DataValidationType, cell?.NativeValue, cell?.TextValue);
            });
    }

    // BL-053 P4b-R1 (additive, read-phase only): reads the target answer cell's FULL data
    // validation rule (type, operator, both formulas, resolved List vocabulary), mirroring the
    // CellRangeInject-P3 target-DV read idiom (inline-List parsed here; range-ref / named-range
    // resolved via the shared DvRangeRefResolver, reused as-is against this task's own _reader
    // and currentPath). The mapper still derives its decision from Type only (byte-equivalent to
    // today) — the richer fields are populated but unread until R2 wires InjectionValueGuard in.
    private IReadOnlyDictionary<int, TargetDvInfo> BuildTargetHLookup(
        string path, string sheetName, RlqV02Config config,
        IReadOnlyList<RlqV02Question> questions)
    {
        if (questions.Count == 0)
            return new Dictionary<int, TargetDvInfo>();

        var col    = config.AnswerColumn;
        var minRow = questions.Min(q => q.RowNumber);
        var maxRow = questions.Max(q => q.RowNumber);
        var cells  = _reader.ReadCells(path, sheetName, [$"{col}{minRow}:{col}{maxRow}"]);

        IReadOnlyList<KeyedTargetDv<int>> keyed = questions
            .Select(q =>
            {
                cells.TryGetValue($"{col}{q.RowNumber}", out var cell);
                return new KeyedTargetDv<int>(q.RowNumber, new TargetDvInfo(
                    cell?.DataValidationType,
                    cell?.DataValidationOperator,
                    cell?.DataValidationFormula,
                    cell?.DataValidationFormula2,
                    cell is null ? null : InlineListValues(cell)));
            })
            .ToList();

        keyed = DvRangeRefResolver.Resolve(
            _reader, path, sheetName, keyed,
            dvTypeSelector:            h => h.Info.Type,
            dvFormulaSelector:         h => h.Info.Formula,
            currentListValuesSelector: h => h.Info.ListValues,
            stampListValues:           (h, vals) => h with { Info = h.Info with { ListValues = vals } });

        return keyed.ToDictionary(h => h.Key, h => h.Info);
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
