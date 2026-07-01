using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ItrqTool.Domain.Validation;

namespace ItrqTool.Infrastructure.Excel;

public sealed class ClosedXmlFeedbackChecklistWriter : IFeedbackChecklistWriter
{
    private static readonly Regex ColumnLetterRegex =
        new(@"^[A-Za-z]{1,3}$", RegexOptions.Compiled);

    public void Populate(
        IReadOnlyList<FeedbackChecklistRow> rows,
        string                              templatePath,
        string                              outputPath,
        FeedbackChecklistWriterOptions      options)
    {
        ValidateColumnMap(options.ColumnMap);

        using var workbook  = RobustWorkbookLoader.Open(templatePath);
        var worksheet = workbook.Worksheet(options.SheetName);

        for (int i = 0; i < rows.Count; i++)
        {
            var row      = rows[i];
            int rowIndex = options.DataStartRow + i;

            foreach (var (column, letter) in options.ColumnMap)
            {
                var value = GetValue(row, column);
                if (value is null) continue;

                var col  = letter.ToUpperInvariant();
                var cell = worksheet.Cell($"{col}{rowIndex}");
                ExcelStyleHelper.WriteWithStylePreservation(worksheet, cell, col, value);
            }
        }

        workbook.SaveAs(outputPath);
    }

    private static object? GetValue(FeedbackChecklistRow row, ChecklistColumn column) =>
        column switch
        {
            ChecklistColumn.Counter        => (object)row.Counter,
            ChecklistColumn.Worksheet      => row.Worksheet,
            ChecklistColumn.QuestionNumber => row.QuestionNumber,
            ChecklistColumn.CellAddresses  => row.CellAddresses,
            ChecklistColumn.QuestionText   => row.QuestionText,
            ChecklistColumn.RequestedData  => row.RequestedData,
            ChecklistColumn.ProvidedBy     => row.ProvidedBy,
            ChecklistColumn.Evaluation     => row.Evaluation.ToString(),
            ChecklistColumn.CheckResult    => row.CheckResult,
            _                              => null
        };

    private static void ValidateColumnMap(IReadOnlyDictionary<ChecklistColumn, string> map)
    {
        var badLetters = map.Values
            .Where(v => !ColumnLetterRegex.IsMatch(v))
            .ToList();
        if (badLetters.Count > 0)
            throw new ArgumentException(
                $"Invalid Excel column letter(s) in map: {string.Join(", ", badLetters.Select(l => $"'{l}'"))}. " +
                "Column letters must be 1–3 alphabetic characters (e.g. \"A\", \"AA\").");

        var duplicates = map.Values
            .GroupBy(v => v.ToUpperInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            throw new ArgumentException(
                $"Duplicate column letter(s) in map: {string.Join(", ", duplicates.Select(l => $"'{l}'"))}. " +
                "Each column must map to a distinct Excel column letter.");
    }
}
