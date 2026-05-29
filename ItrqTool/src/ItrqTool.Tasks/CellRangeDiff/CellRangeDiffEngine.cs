using ItrqTool.Domain;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.CellRangeDiff;

public static class CellRangeDiffEngine
{
    private static readonly ExcelCellStructure Empty = new(null, null, null, null);

    public static CellRangeDiffResult Diff(
        IReadOnlyDictionary<string, ExcelCellStructure> file1,
        IReadOnlyDictionary<string, ExcelCellStructure> file2,
        IReadOnlyList<string> orderedAddresses,
        CompareScope scope)
    {
        var changed = new List<ChangedCell>();
        var unchanged = new List<UnchangedCell>();

        foreach (var address in orderedAddresses)
        {
            var c1 = file1.TryGetValue(address, out var v1) ? v1 : Empty;
            var c2 = file2.TryGetValue(address, out var v2) ? v2 : Empty;

            // Trim before comparing — leading/trailing whitespace is not a meaningful change
            bool textChanged = !string.Equals(
                (c1.TextValue ?? "").Trim(),
                (c2.TextValue ?? "").Trim(),
                StringComparison.Ordinal);

            bool dvChanged = false;
            bool cfChanged = false;

            if (scope == CompareScope.ValueAndDvCf)
            {
                dvChanged = DvComparer.IsDvChangedFull(
                    c1.DataValidationType, c1.DataValidationOperator,
                    c1.DataValidationFormula, c1.DataValidationFormula2,
                    c2.DataValidationType, c2.DataValidationOperator,
                    c2.DataValidationFormula, c2.DataValidationFormula2);

                cfChanged = CfComparer.IsCfChanged(
                    c1.ConditionalFormattingType, c1.ConditionalFormattingOperator,
                    c1.ConditionalFormattingValue, c1.ConditionalFormattingValue2,
                    c2.ConditionalFormattingType, c2.ConditionalFormattingOperator,
                    c2.ConditionalFormattingValue, c2.ConditionalFormattingValue2);
            }

            if (textChanged || dvChanged || cfChanged)
                changed.Add(new ChangedCell(address, c1, c2, textChanged, dvChanged, cfChanged));
            else
                unchanged.Add(new UnchangedCell(address, c1));
        }

        return new CellRangeDiffResult(changed, unchanged);
    }
}
