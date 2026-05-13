namespace ItrqTool.Domain;

public record ExcelCellValue(object? Value, Type? ClrType)
{
    public T? As<T>() => Value is T v ? v : default;
}
