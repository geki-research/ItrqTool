using ItrqTool.Domain;

namespace ItrqTool.Infrastructure;

public sealed class ClosedXmlExcelReader : IExcelReader
{
    public IReadOnlyList<string> GetSheetNames(string filePath)
        => throw new NotImplementedException();

    public ExcelSheet ReadSheet(string filePath, string sheetName, bool firstRowIsHeader = true)
        => throw new NotImplementedException();

    public IReadOnlyList<ExcelSheet> ReadAllSheets(string filePath, bool firstRowIsHeader = true)
        => throw new NotImplementedException();
}
