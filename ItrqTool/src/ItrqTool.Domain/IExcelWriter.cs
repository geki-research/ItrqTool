namespace ItrqTool.Domain;

public interface IExcelWriter
{
    void WriteWorkbook(ExcelWorkbookData data, string filePath);
}

public record ExcelWorkbookData(IReadOnlyList<ExcelSheetData> Sheets);

public record ExcelSheetData(
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows
);
