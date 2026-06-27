using ItrqTool.Domain;

namespace ItrqTool.Tasks.WorksheetStructure;

/// <summary>
/// The format-version strategy seam. A schema's <see cref="WorksheetStructureSchema.SchemaFormatVersion"/>
/// selects exactly one strategy; an unknown version is rejected loudly (AssetError) by the mediator.
/// There is exactly ONE strategy today (<see cref="SchemaVerificationStrategyV1"/>).
/// </summary>
public interface ISchemaVerificationStrategy
{
    int SchemaFormatVersion { get; }

    /// <summary>
    /// PURE (no IO): compares the already-read <paramref name="headerCells"/> (keyed by UPPER A1 address,
    /// from <c>IExcelStructureReader.ReadCells</c>) against <paramref name="schema"/> and returns a
    /// Match or Mismatch result. <paramref name="filePath"/> is for diagnostics only.
    /// </summary>
    WorksheetStructureResult Verify(
        WorksheetStructureSchema schema,
        IReadOnlyDictionary<string, ExcelCellStructure> headerCells,
        string filePath);
}
