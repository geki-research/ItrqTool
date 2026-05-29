using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Domain.Reporting;
using ItrqTool.Tasks.CellRangeDiff;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks;

public sealed class CellRangeDiffTask : IWorkflowTask
{
    private const string DefaultReportTitle = "Cell Range Diff Report";

    private static readonly Regex RangeTokenRegex =
        new(@"^[A-Za-z]{1,3}[0-9]+(:[A-Za-z]{1,3}[0-9]+)?$", RegexOptions.Compiled);

    private static readonly Regex AddressRegex =
        new(@"^([A-Za-z]+)(\d+)$", RegexOptions.Compiled);

    private readonly IExcelStructureReader _structureReader;
    private readonly IHtmlCellRangeDiffReportWriter _htmlReportWriter;
    private readonly ILogger<CellRangeDiffTask> _logger;

    public CellRangeDiffTask(
        IExcelStructureReader structureReader,
        IHtmlCellRangeDiffReportWriter htmlReportWriter,
        ILogger<CellRangeDiffTask> logger)
    {
        _structureReader = structureReader;
        _htmlReportWriter = htmlReportWriter;
        _logger = logger;
    }

    public string TaskType => "CellRangeDiff";

    public Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Validate required parameters — surface all missing in one Error
            var missingParams = new List<string>();
            if (!TryGetParam(ctx, "file1Path",    out var file1Path))      missingParams.Add("file1Path");
            if (!TryGetParam(ctx, "file2Path",    out var file2Path))      missingParams.Add("file2Path");
            if (!TryGetParam(ctx, "sheet1Name",   out var sheet1Name))     missingParams.Add("sheet1Name");
            if (!TryGetParam(ctx, "sheet2Name",   out var sheet2Name))     missingParams.Add("sheet2Name");
            if (!TryGetParam(ctx, "ranges",       out var rangesRaw))      missingParams.Add("ranges");
            if (!TryGetParam(ctx, "compareScope", out var compareScopeRaw)) missingParams.Add("compareScope");

            if (missingParams.Count > 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Required parameter(s) missing or empty: {string.Join(", ", missingParams)}.",
                    DateTimeOffset.Now));
                return Task.FromResult(new TaskResult(Succeeded: false, messages, sw.Elapsed));
            }

            // 2. Parse compareScope
            if (!Enum.TryParse<CompareScope>(compareScopeRaw, ignoreCase: true, out var scope))
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Unrecognised compareScope value '{compareScopeRaw}'. Valid values: Value, ValueAndDvCf.",
                    DateTimeOffset.Now));
                return Task.FromResult(new TaskResult(Succeeded: false, messages, sw.Elapsed));
            }

            // 3. Parse and validate ranges
            var rangeTokens = rangesRaw.Split(';')
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();

            if (rangeTokens.Count == 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    "Parameter 'ranges' contains no valid range tokens.", DateTimeOffset.Now));
                return Task.FromResult(new TaskResult(Succeeded: false, messages, sw.Elapsed));
            }

            var badTokens = rangeTokens.Where(t => !RangeTokenRegex.IsMatch(t)).ToList();
            if (badTokens.Count > 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Malformed range token(s): {string.Join(", ", badTokens)}.", DateTimeOffset.Now));
                return Task.FromResult(new TaskResult(Succeeded: false, messages, sw.Elapsed));
            }

            // 4. Optional reportTitle
            ctx.Parameters.TryGetValue("reportTitle", out var reportTitleRaw);
            var reportTitle = string.IsNullOrWhiteSpace(reportTitleRaw) ? DefaultReportTitle : reportTitleRaw;

            // 5. File existence
            var missingFiles = new List<string>();
            if (!File.Exists(file1Path)) missingFiles.Add($"file1Path: {file1Path}");
            if (!File.Exists(file2Path)) missingFiles.Add($"file2Path: {file2Path}");
            if (missingFiles.Count > 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    "File(s) not found: " + string.Join("; ", missingFiles), DateTimeOffset.Now));
                return Task.FromResult(new TaskResult(Succeeded: false, messages, sw.Elapsed));
            }

            ct.ThrowIfCancellationRequested();

            // 6. Read cells (missing sheet propagates as exception → caught below)
            _logger.LogInformation("Reading cells from file1: {Path}", file1Path);
            var cells1 = _structureReader.ReadCells(file1Path, sheet1Name, rangeTokens);

            ct.ThrowIfCancellationRequested();

            _logger.LogInformation("Reading cells from file2: {Path}", file2Path);
            var cells2 = _structureReader.ReadCells(file2Path, sheet2Name, rangeTokens);

            ct.ThrowIfCancellationRequested();

            // 7. Build ordered address list: union of keys, sort by row asc then column index asc
            var allAddresses = cells1.Keys
                .Concat(cells2.Keys)
                .Select(a => a.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(ParseRow)
                .ThenBy(a => ParseColIndex(ParseCol(a)))
                .ToList();

            // 8. Diff
            var result = CellRangeDiffEngine.Diff(cells1, cells2, allAddresses, scope);

            // 9. Build and write report
            var reportPath = ctx.OutputPaths["report"];
            var reportData = BuildReportData(
                reportTitle, file1Path, file2Path, sheet1Name, sheet2Name, scope, result);
            _htmlReportWriter.WriteReport(reportData, reportPath);

            // 10. Curated messages
            messages.Add(new(MessageSeverity.Info,
                $"Compared cells across {rangeTokens.Count} range(s).", DateTimeOffset.Now));
            messages.Add(new(MessageSeverity.Info,
                $"{result.Changed.Count} changed, {result.Unchanged.Count} unchanged.", DateTimeOffset.Now));
            messages.Add(new(MessageSeverity.Info,
                $"Report written to {Path.GetFileName(reportPath)}", DateTimeOffset.Now));

            return Task.FromResult(new TaskResult(Succeeded: true, messages, sw.Elapsed));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            messages.Add(new(MessageSeverity.Error, ex.Message, DateTimeOffset.Now));
            return Task.FromResult(new TaskResult(Succeeded: false, messages, sw.Elapsed));
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────────

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

    private static HtmlDiffCellRangeReportData BuildReportData(
        string title,
        string file1Path, string file2Path,
        string sheet1Name, string sheet2Name,
        CompareScope scope,
        CellRangeDiffResult result)
    {
        bool includeVF = scope == CompareScope.ValueAndDvCf;

        var changed = result.Changed.Select(c =>
        {
            var col = ParseCol(c.Address);
            var row = ParseRow(c.Address);
            var f1dv = includeVF ? DvDisplayFormatter.FormatFull(
                c.Cell1.DataValidationType, c.Cell1.DataValidationOperator,
                c.Cell1.DataValidationFormula, c.Cell1.DataValidationFormula2) : "—";
            var f2dv = includeVF ? DvDisplayFormatter.FormatFull(
                c.Cell2.DataValidationType, c.Cell2.DataValidationOperator,
                c.Cell2.DataValidationFormula, c.Cell2.DataValidationFormula2) : "—";
            var f1cf = includeVF ? CfDisplayFormatter.Format(
                c.Cell1.ConditionalFormattingType, c.Cell1.ConditionalFormattingOperator,
                c.Cell1.ConditionalFormattingValue, c.Cell1.ConditionalFormattingValue2) : "—";
            var f2cf = includeVF ? CfDisplayFormatter.Format(
                c.Cell2.ConditionalFormattingType, c.Cell2.ConditionalFormattingOperator,
                c.Cell2.ConditionalFormattingValue, c.Cell2.ConditionalFormattingValue2) : "—";
            return new HtmlDiffCellRangeChangedCell(
                c.Address, col, row,
                c.Cell1.TextValue, c.Cell2.TextValue, c.TextChanged,
                f1dv, f2dv, c.DvChanged,
                f1cf, f2cf, c.CfChanged);
        }).ToList();

        var unchanged = result.Unchanged.Select(u =>
        {
            var col = ParseCol(u.Address);
            var row = ParseRow(u.Address);
            var dvDisplay = includeVF ? DvDisplayFormatter.FormatFull(
                u.Cell.DataValidationType, u.Cell.DataValidationOperator,
                u.Cell.DataValidationFormula, u.Cell.DataValidationFormula2) : "—";
            var cfDisplay = includeVF ? CfDisplayFormatter.Format(
                u.Cell.ConditionalFormattingType, u.Cell.ConditionalFormattingOperator,
                u.Cell.ConditionalFormattingValue, u.Cell.ConditionalFormattingValue2) : "—";
            return new HtmlDiffCellRangeUnchangedCell(
                u.Address, col, row, u.Cell.TextValue, dvDisplay, cfDisplay);
        }).ToList();

        return new HtmlDiffCellRangeReportData(
            title, file1Path, file2Path, sheet1Name, sheet2Name,
            includeVF, DateTimeOffset.Now, changed, unchanged);
    }

    private static string ParseCol(string address)
    {
        var m = AddressRegex.Match(address);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : address;
    }

    private static int ParseRow(string address)
    {
        var m = AddressRegex.Match(address);
        return m.Success && int.TryParse(m.Groups[2].Value, out var r) ? r : 0;
    }

    private static int ParseColIndex(string col)
    {
        int idx = 0;
        foreach (var c in col.ToUpperInvariant())
            idx = idx * 26 + (c - 'A' + 1);
        return idx;
    }
}
