namespace ItrqTool.Domain;

public interface IExcelReader
{
    IReadOnlyList<string> GetSheetNames(string filePath);
    ExcelSheet ReadSheet(string filePath, string sheetName, bool firstRowIsHeader = true);
    IReadOnlyList<ExcelSheet> ReadAllSheets(string filePath, bool firstRowIsHeader = true);
}
