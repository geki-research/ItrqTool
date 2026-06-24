namespace ItrqTool.Domain;

public interface IExcelTemplateWriter
{
    // Opens `templatePath`, writes each (Row, Column) → Value into `sheetName` preserving the cell's
    // effective formatting, and saves the result to `outputPath`. The template file is not modified in place.
    void Populate(
        string templatePath,
        string sheetName,
        IReadOnlyList<CellWriteEntry> cells,
        string outputPath);
}

public record CellWriteEntry(int Row, string Column, string Value, object? TypedValue = null);
