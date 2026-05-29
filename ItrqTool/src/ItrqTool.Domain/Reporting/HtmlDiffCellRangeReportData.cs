namespace ItrqTool.Domain.Reporting;

public record HtmlDiffCellRangeReportData(
    string Title,
    string File1Path,
    string File2Path,
    string Sheet1Name,
    string Sheet2Name,
    bool IncludeValidationFormatting,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<HtmlDiffCellRangeChangedCell> Changed,
    IReadOnlyList<HtmlDiffCellRangeUnchangedCell> Unchanged);

public record HtmlDiffCellRangeChangedCell(
    string Address, string Column, int Row,
    string? File1Value, string? File2Value, bool TextChanged,
    string File1DvDisplay, string File2DvDisplay, bool DvChanged,
    string File1CfDisplay, string File2CfDisplay, bool CfChanged);

public record HtmlDiffCellRangeUnchangedCell(
    string Address, string Column, int Row,
    string? Value, string DvDisplay, string CfDisplay);
