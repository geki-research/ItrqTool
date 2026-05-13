namespace ItrqTool.Domain;

public record ExcelSheet(
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<ExcelCellValue>> Rows
);
