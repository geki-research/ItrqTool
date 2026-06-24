using System.Diagnostics;
using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;

namespace ItrqTool.Tasks.RiskLevelQuestionInject;

/// <summary>
/// RLQ inject (v01 → v02). Reads the previous-year v01 response and the current-year v02
/// template, aligns them with <see cref="CrossFormatAligner"/>, maps the confident matches
/// to reference cell writes via <see cref="RlqInjectMapper"/>, and writes the populated
/// workbook to the working-dir <c>output</c> path. Final placement is the downstream
/// StaticFileSink's job — this task performs NO File.* / SaveAs.
/// </summary>
/// <remarks>
/// Parameters: <c>configurationFullFilename</c> (full path to the inject config; the two
/// referenced validation configs resolve relative to ITS directory).
/// Inputs: <c>previousResponse</c> (v01), <c>currentTemplate</c> (v02).
/// Output: <c>output</c>.
/// Succeeded semantics: false only on missing inputs/params, missing/invalid config files,
/// or parse errors. Type-compatibility policy messages (Warning/Error) do not fail the task.
/// </remarks>
public sealed class RiskLevelQuestionInjectV01ToV02Task : IWorkflowTask
{
    private readonly IExcelStructureReader _reader;
    private readonly IExcelTemplateWriter _writer;

    public RiskLevelQuestionInjectV01ToV02Task(
        IExcelStructureReader reader,
        IExcelTemplateWriter writer)
    {
        _reader = reader;
        _writer = writer;
    }

    public string TaskType => "RiskLevelQuestionInject_v01_to_v02";

    public async Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        try
        {
            if (!TryGetParam(ctx, "configurationFullFilename", out var injectConfigPath))
            {
                messages.Add(new(MessageSeverity.Error,
                    "Required parameter missing or empty: configurationFullFilename.",
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

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
            messages.Add(new(MessageSeverity.Error, ex.Message, DateTimeOffset.Now));
            return new TaskResult(Succeeded: false, messages, sw.Elapsed);
        }
    }

    private IReadOnlyDictionary<int, (string? DvType, object? Native)> BuildSourceHLookup(
        string path, string sheetName, RlqV01Config config,
        IReadOnlyList<RlqV01Question> questions)
    {
        if (questions.Count == 0)
            return new Dictionary<int, (string?, object?)>();

        var col    = config.AnswerColumn;
        var minRow = questions.Min(q => q.RowNumber);
        var maxRow = questions.Max(q => q.RowNumber);
        var cells  = _reader.ReadCells(path, sheetName, [$"{col}{minRow}:{col}{maxRow}"]);

        return questions.ToDictionary(
            q => q.RowNumber,
            q =>
            {
                cells.TryGetValue($"{col}{q.RowNumber}", out var cell);
                return (cell?.DataValidationType, cell?.NativeValue);
            });
    }

    private IReadOnlyDictionary<int, string?> BuildTargetHLookup(
        string path, string sheetName, RlqV02Config config,
        IReadOnlyList<RlqV02Question> questions)
    {
        if (questions.Count == 0)
            return new Dictionary<int, string?>();

        var col    = config.AnswerColumn;
        var minRow = questions.Min(q => q.RowNumber);
        var maxRow = questions.Max(q => q.RowNumber);
        var cells  = _reader.ReadCells(path, sheetName, [$"{col}{minRow}:{col}{maxRow}"]);

        return questions.ToDictionary(
            q => q.RowNumber,
            q =>
            {
                cells.TryGetValue($"{col}{q.RowNumber}", out var cell);
                return cell?.DataValidationType;
            });
    }

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
