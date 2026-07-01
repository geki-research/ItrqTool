using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Tasks.CellRangeInject;

namespace ItrqTool.Tasks;

public sealed class CellRangeInjectTask : IWorkflowTask
{
    private readonly IExcelStructureReader _reader;
    private readonly IExcelTemplateWriter _writer;
    private readonly ILogger<CellRangeInjectTask> _logger;

    public CellRangeInjectTask(
        IExcelStructureReader reader,
        IExcelTemplateWriter writer,
        ILogger<CellRangeInjectTask> logger)
    {
        _reader = reader;
        _writer = writer;
        _logger = logger;
    }

    public string TaskType => "CellRangeInject";

    public Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Required parameters — collect all missing in one Error.
            var missingParams = new List<string>();
            if (!TryGetParam(ctx, "mappings",         out var mappingsRaw))      missingParams.Add("mappings");
            if (!TryGetParam(ctx, "sourceSheetName",  out var sourceSheetName))  missingParams.Add("sourceSheetName");
            if (!TryGetParam(ctx, "targetSheetName",  out var targetSheetName))  missingParams.Add("targetSheetName");
            if (missingParams.Count > 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Required parameter(s) missing or empty: {string.Join(", ", missingParams)}.",
                    DateTimeOffset.Now));
                return Task.FromResult(new TaskResult(Succeeded: false, messages, sw.Elapsed));
            }

            // 2. Required inputs — collect all missing.
            var missingInputs = new List<string>();
            if (!TryGetInput(ctx, "source",         out var sourcePath))         missingInputs.Add("source");
            if (!TryGetInput(ctx, "targetTemplate", out var targetTemplatePath)) missingInputs.Add("targetTemplate");
            if (missingInputs.Count > 0)
            {
                foreach (var k in missingInputs)
                    messages.Add(new(MessageSeverity.Error,
                        $"Required input missing or empty: {k}.", DateTimeOffset.Now));
                return Task.FromResult(new TaskResult(Succeeded: false, messages, sw.Elapsed));
            }

            // 3. Parse mappings — fail before any IO on grammar or dimension errors.
            var parseResult = CellMappingParser.Parse(mappingsRaw);
            if (parseResult.Errors.Count > 0)
            {
                foreach (var e in parseResult.Errors)
                    messages.Add(new(MessageSeverity.Error, e, DateTimeOffset.Now));
                return Task.FromResult(new TaskResult(Succeeded: false, messages, sw.Elapsed));
            }

            // 4. File existence.
            var missingFiles = new List<string>();
            if (!File.Exists(sourcePath))         missingFiles.Add($"source: {sourcePath}");
            if (!File.Exists(targetTemplatePath)) missingFiles.Add($"targetTemplate: {targetTemplatePath}");
            if (missingFiles.Count > 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    "File(s) not found: " + string.Join("; ", missingFiles), DateTimeOffset.Now));
                return Task.FromResult(new TaskResult(Succeeded: false, messages, sw.Elapsed));
            }

            ct.ThrowIfCancellationRequested();

            // 5. Read source cells (missing sheet propagates as exception → caught below).
            var a1Ranges = parseResult.Pairs
                .Select(p => $"{p.SourceColumn}{p.SourceRow}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation("Reading {Count} source address(es) from '{Sheet}' in {Path}",
                a1Ranges.Count, sourceSheetName, sourcePath);
            _logger.LogInformation("source worksheet sought: '{Sheet}'", sourceSheetName);

            IReadOnlyList<string> sourceWorksheets;
            try
            {
                sourceWorksheets = _reader.GetWorksheetNames(sourcePath);
                _logger.LogInformation("source workbook worksheets: [ {Names} ]", FormatNames(sourceWorksheets));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "failed to open source workbook '{Path}' to list worksheets: {Message}",
                    sourcePath, ex.Message);
                throw;
            }

            IReadOnlyDictionary<string, ExcelCellStructure> srcCells;
            try
            {
                srcCells = _reader.ReadCells(sourcePath, sourceSheetName, a1Ranges);
                _logger.LogInformation("source worksheet '{Sheet}' found", sourceSheetName);
                _logger.LogInformation("read {Count} source cell(s)", srcCells.Count);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "source worksheet '{Sheet}' NOT FOUND in {Path}; available worksheets: [ {Names} ]",
                    sourceSheetName, sourcePath, FormatNames(sourceWorksheets));
                throw;
            }

            // 6. Build write entries — native value carries via TypedValue so the target number format is preserved.
            var cells = parseResult.Pairs.Select(p =>
            {
                var key = $"{p.SourceColumn}{p.SourceRow}".ToUpperInvariant();
                srcCells.TryGetValue(key, out var src);
                return new CellWriteEntry(
                    Row: p.TargetRow,
                    Column: p.TargetColumn,
                    Value: src?.TextValue ?? "",
                    TypedValue: src?.NativeValue);
            }).ToList();

            _logger.LogInformation("target template path: {Path}", targetTemplatePath);
            _logger.LogInformation("target worksheet sought: '{Sheet}'", targetSheetName);

            IReadOnlyList<string> targetWorksheets;
            try
            {
                targetWorksheets = _reader.GetWorksheetNames(targetTemplatePath);
                _logger.LogInformation("target workbook worksheets: [ {Names} ]", FormatNames(targetWorksheets));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "failed to open target template '{Path}' to list worksheets: {Message}",
                    targetTemplatePath, ex.Message);
                throw;
            }

            // 7. Write — task does NO File.* / SaveAs; StaticFileSink owns placement.
            //    Missing target sheet throws ArgumentException → caught below.
            try
            {
                _writer.Populate(targetTemplatePath, targetSheetName, cells, ctx.OutputPaths["output"]);
                _logger.LogInformation("target worksheet '{Sheet}' found", targetSheetName);
                _logger.LogInformation("writing {Count} cell(s) to target worksheet '{Sheet}'",
                    cells.Count, targetSheetName);
                _logger.LogInformation("wrote output to '{OutputPath}'", ctx.OutputPaths["output"]);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "target worksheet '{Sheet}' NOT FOUND in {Path}; available worksheets: [ {Names} ]",
                    targetSheetName, targetTemplatePath, FormatNames(targetWorksheets));
                throw;
            }

            messages.Add(new(MessageSeverity.Info,
                $"Injected {cells.Count} cell(s) from '{sourceSheetName}' into '{targetSheetName}'.",
                DateTimeOffset.Now));

            _logger.LogInformation("CellRangeInject completed: {Count} cell(s) written", cells.Count);

            return Task.FromResult(new TaskResult(Succeeded: true, messages, sw.Elapsed));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            messages.Add(new(MessageSeverity.Error, ex.Message, DateTimeOffset.Now));
            return Task.FromResult(new TaskResult(Succeeded: false, messages, sw.Elapsed));
        }
    }

    private static string FormatNames(IReadOnlyList<string> names)
        => string.Join(", ", names.Select(n => $"'{n}'"));

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

    private static bool TryGetInput(TaskExecutionContext ctx, string key, out string value)
    {
        if (ctx.InputPaths.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
        {
            value = v;
            return true;
        }
        value = string.Empty;
        return false;
    }
}
